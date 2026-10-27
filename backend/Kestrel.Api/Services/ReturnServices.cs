using FluentValidation;
using Kestrel.Api.Domain;
using Kestrel.Api.Dtos;
using Kestrel.Api.Infrastructure;
using Kestrel.Api.Repositories;
using Microsoft.Extensions.Options;

namespace Kestrel.Api.Services;

public interface IReturnService
{
    Task<EligibilityDto> GetEligibilityAsync(string orderNumber);
    Task<ReturnDto> CreateAsync(CreateReturnDto dto);
    Task<ReturnDto> GetAsync(int id);
    Task<ReturnDto> ApproveAsync(int id, int userId);
    Task<ReturnDto> RejectAsync(int id, int userId);
    Task<bool> IsSerialReturnerAsync(int customerId);
    Task<ReturnsDashboardDto> GetDashboardAsync(DateOnly from, DateOnly to);
}

public class ReturnService : IReturnService
{
    private readonly IReturnRepository _returns;
    private readonly IOrderRepository _orders;
    private readonly ICustomerRepository _customers;
    private readonly IStoreRepository _stores;
    private readonly ICategoryRepository _categories;
    private readonly IStockRepository _stock;
    private readonly IUserRepository _users;
    private readonly IValidator<CreateReturnDto> _createValidator;
    private readonly IValidator<ApproveReturnDto> _approveValidator;
    private readonly ReturnsOptions _options;
    private readonly TimeProvider _clock;

    public ReturnService(IReturnRepository returns, IOrderRepository orders, ICustomerRepository customers,
        IStoreRepository stores, ICategoryRepository categories, IStockRepository stock, IUserRepository users,
        IValidator<CreateReturnDto> createValidator, IValidator<ApproveReturnDto> approveValidator,
        IOptions<ReturnsOptions> options, TimeProvider clock)
    {
        _returns = returns;
        _orders = orders;
        _customers = customers;
        _stores = stores;
        _categories = categories;
        _stock = stock;
        _users = users;
        _createValidator = createValidator;
        _approveValidator = approveValidator;
        _options = options.Value;
        _clock = clock;
    }

    private DateTime NowUtc => _clock.GetUtcNow().UtcDateTime;
    private DateOnly Today => DateOnly.FromDateTime(NowUtc);

    // Everything the rules need to know about one order: what was paid per line and what has been returned so far.
    private sealed class LineState
    {
        public required OrderLine Line { get; init; }
        public required Category Category { get; init; }
        public decimal Paid { get; init; }
        public int ReturnedQuantity { get; set; }
        public decimal Refunded { get; set; }
        public DateOnly ReturnableUntil { get; init; }
    }

    private sealed class OrderState
    {
        public required Order Order { get; init; }
        public required List<LineState> Lines { get; init; }
        public decimal CapturedPayments { get; init; }
        public decimal RefundedTotal { get; set; }
        public decimal OrderRemaining => Math.Max(0m, CapturedPayments - RefundedTotal);
    }

    private async Task<OrderState> LoadStateAsync(Order order)
    {
        var categories = new Dictionary<int, Category>();
        var paid = ReturnPricing.PaidPerLine(order);
        var lines = new List<LineState>();
        foreach (var l in order.Lines.OrderBy(x => x.Id))
        {
            var categoryId = l.Product?.CategoryId ?? throw new BusinessRuleException($"Product {l.ProductId} is missing.");
            if (!categories.TryGetValue(categoryId, out var category))
            {
                category = await _categories.GetAsync(categoryId) ?? throw new NotFoundException($"Category {categoryId} was not found.");
                categories[categoryId] = category;
            }
            lines.Add(new LineState
            {
                Line = l,
                Category = category,
                Paid = paid[l.Id],
                // BR-01: returnable while today (UTC) <= ordered date + window days.
                ReturnableUntil = DateOnly.FromDateTime(order.OrderedUtc.Date.AddDays(category.ReturnWindowDays))
            });
        }

        var state = new OrderState
        {
            Order = order,
            Lines = lines,
            CapturedPayments = order.Payments.Where(p => p.Status == "CAPTURED").Sum(p => p.Amount)
        };

        // DRAFT and APPROVED RMAs both count against the caps (BR-03, BR-05); REJECTED ones do not.
        foreach (var rma in await _returns.ListForOrderAsync(order.Id))
        {
            if (rma.Status == ReturnStatus.Rejected) continue;
            foreach (var rl in rma.Lines)
            {
                var ls = lines.FirstOrDefault(x => x.Line.Id == rl.OrderLineId);
                if (ls == null) continue;
                ls.ReturnedQuantity += rl.Quantity;
                ls.Refunded += rl.RefundAmount;
                state.RefundedTotal += rl.RefundAmount;
            }
        }
        return state;
    }

    // BR-02
    private static string? OrderStatusReason(Order order) =>
        order.Status == OrderStatus.Paid || order.Status == OrderStatus.Fulfilled
            ? null
            : $"A {order.Status} order cannot be returned.";

    private static string? LineReason(LineState ls, DateOnly today, string? orderReason)
    {
        if (orderReason != null) return orderReason;
        if (today > ls.ReturnableUntil) return $"The {ls.Category.ReturnWindowDays}-day return window ended on {ls.ReturnableUntil:yyyy-MM-dd}.";
        if (ls.Line.Quantity - ls.ReturnedQuantity <= 0) return "Every item on this line has already been returned.";
        return null;
    }

    public async Task<EligibilityDto> GetEligibilityAsync(string orderNumber)
    {
        var order = await _orders.GetWithDetailsByNumberAsync(orderNumber?.Trim() ?? "")
            ?? throw new NotFoundException($"Order {orderNumber} was not found.");
        var state = await LoadStateAsync(order);
        var customer = await _customers.GetReadOnlyAsync(order.CustomerId);
        var orderReason = OrderStatusReason(order);
        var today = Today;

        var lines = state.Lines.Select(ls =>
        {
            var reason = LineReason(ls, today, orderReason);
            var qty = reason == null ? ls.Line.Quantity - ls.ReturnedQuantity : 0;
            var amount = reason == null ? Math.Min(ls.Paid - ls.Refunded, state.OrderRemaining) : 0m;
            return new EligibilityLineDto(ls.Line.Id, ls.Line.Product?.Sku ?? "", ls.Line.Product?.Name ?? "", ls.Category.Code,
                ls.Category.ReturnWindowDays, ls.Line.Quantity, ls.ReturnedQuantity, qty, ls.Paid, ls.Refunded,
                Math.Max(0m, amount), ls.ReturnableUntil, reason == null, reason);
        }).ToList();

        var eligible = lines.Any(l => l.Eligible);
        string? overall = null;
        if (!eligible)
            overall = orderReason ?? lines.Select(l => l.Reason).FirstOrDefault(r => r != null) ?? "The order has no lines.";

        return new EligibilityDto(order.Id, order.OrderNumber, order.Status,
            customer == null ? "" : $"{customer.FirstName} {customer.LastName}", order.OrderedUtc, order.StoreId,
            eligible, overall, lines.Count == 0 ? null : lines.Max(l => l.ReturnableUntil),
            state.CapturedPayments, state.RefundedTotal, state.OrderRemaining,
            await IsSerialReturnerAsync(order.CustomerId), lines);
    }

    public async Task<ReturnDto> CreateAsync(CreateReturnDto dto)
    {
        await _createValidator.ValidateAndThrowAsync(dto);
        var order = await _orders.GetWithDetailsByNumberAsync(dto.OrderNumber.Trim())
            ?? throw new NotFoundException($"Order {dto.OrderNumber} was not found.");
        var store = await _stores.GetAsync(dto.StoreId) ?? throw new NotFoundException($"Store {dto.StoreId} was not found.");
        if (!store.IsActive) throw new BusinessRuleException($"Store {store.Name} is closed.");
        if (await _users.GetAsync(dto.CreatedByUserId) == null) throw new NotFoundException($"User {dto.CreatedByUserId} was not found.");

        var orderReason = OrderStatusReason(order);
        if (orderReason != null) throw new BusinessRuleException(orderReason);

        // Serialise concurrent RMAs on the same order: the order row lock is held until commit, so the cap check
        // below (read) and the insert (write) cannot interleave with another request. Earlier DRAFTs for this
        // order are superseded (auto-rejected) so repeated "Calculate refund" clicks do not hold quantity forever.
        var created = await _returns.InTransactionAsync(async () =>
        {
        await _returns.LockOrderAsync(order.Id);
        var drafts = (await _returns.ListForOrderAsync(order.Id)).Where(r => r.Status == ReturnStatus.Draft).ToList();
        foreach (var d in drafts)
        {
            var tracked = await _returns.GetWithLinesForUpdateAsync(d.Id);
            if (tracked != null && tracked.Status == ReturnStatus.Draft) tracked.Status = ReturnStatus.Rejected;
        }
        if (drafts.Count > 0) await _returns.SaveAsync();

        var state = await LoadStateAsync(order);
        var today = Today;
        var rma = new Return
        {
            OrderId = order.Id,
            StoreId = store.Id,
            CustomerId = order.CustomerId,
            Status = ReturnStatus.Draft,
            CreatedByUserId = dto.CreatedByUserId,
            CreatedUtc = NowUtc
        };

        foreach (var input in dto.Lines)
        {
            var ls = state.Lines.FirstOrDefault(x => x.Line.Id == input.OrderLineId)
                ?? throw new BusinessRuleException($"Order line {input.OrderLineId} does not belong to order {order.OrderNumber}.");
            var name = ls.Line.Product?.Sku ?? $"line {ls.Line.Id}";

            // BR-01
            if (today > ls.ReturnableUntil)
                throw new BusinessRuleException($"{name}: the {ls.Category.ReturnWindowDays}-day return window ended on {ls.ReturnableUntil:yyyy-MM-dd}.");
            // BR-03
            var remainingQty = ls.Line.Quantity - ls.ReturnedQuantity;
            if (input.Quantity > remainingQty)
                throw new BusinessRuleException($"{name}: only {remainingQty} of {ls.Line.Quantity} can still be returned.");

            var grade = input.Grade.Trim().ToUpperInvariant();
            // BR-04, BR-05 (line level)
            var amount = ReturnPricing.RefundForLine(ls.Paid, ls.Line.Quantity, ls.ReturnedQuantity, ls.Refunded,
                input.Quantity, grade, _options.GradeCRefundPercent);

            rma.Lines.Add(new ReturnLine { OrderLineId = ls.Line.Id, Quantity = input.Quantity, Grade = grade, RefundAmount = amount });
            ls.ReturnedQuantity += input.Quantity;
            ls.Refunded += amount;
            state.RefundedTotal += amount;
        }

        rma.RefundTotal = rma.Lines.Sum(l => l.RefundAmount);
        // BR-05 (order level): never more than the captured payments.
        if (state.RefundedTotal > state.CapturedPayments)
            throw new BusinessRuleException("The refund would be more than the customer paid for this order.");

        rma.RmaNumber = await NextRmaNumberAsync(rma.CreatedUtc);
        await _returns.AddAsync(rma);
        await _returns.SaveAsync();
        return rma;
        });
        return await GetAsync(created.Id);
    }

    private async Task<string> NextRmaNumberAsync(DateTime now)
    {
        while (true)
        {
            var candidate = $"RMA-{now:yyyyMMdd}-{Random.Shared.Next(1000, 9999)}";
            if (!await _returns.RmaNumberExistsAsync(candidate)) return candidate;
        }
    }

    public async Task<ReturnDto> GetAsync(int id)
    {
        var rma = await _returns.GetWithLinesAsync(id) ?? throw new NotFoundException($"Return {id} was not found.");
        var order = await _orders.GetWithDetailsAsync(rma.OrderId) ?? throw new NotFoundException($"Order {rma.OrderId} was not found.");
        var customer = await _customers.GetReadOnlyAsync(rma.CustomerId);
        var store = await _stores.GetAsync(rma.StoreId);
        var lines = rma.Lines.OrderBy(l => l.Id).Select(l =>
        {
            var ol = order.Lines.FirstOrDefault(x => x.Id == l.OrderLineId);
            return new ReturnLineDto(l.Id, l.OrderLineId, ol?.Product?.Sku ?? "", ol?.Product?.Name ?? "", l.Quantity, l.Grade,
                l.RefundAmount, l.Restocked, l.SupplierClaim);
        }).ToList();
        return new ReturnDto(rma.Id, rma.RmaNumber, rma.OrderId, order.OrderNumber,
            customer == null ? "" : $"{customer.FirstName} {customer.LastName}", rma.StoreId, store?.Name ?? "",
            rma.Status, rma.RefundTotal, rma.RefundTotal > _options.ManagerApprovalThreshold,
            rma.CreatedByUserId, rma.ApprovedByUserId, rma.CreatedUtc, rma.ApprovedUtc,
            await IsSerialReturnerAsync(rma.CustomerId), lines);
    }

    public async Task<ReturnDto> ApproveAsync(int id, int userId)
    {
        await _approveValidator.ValidateAndThrowAsync(new ApproveReturnDto(userId));
        // The RMA row is locked (SELECT ... FOR UPDATE) for the whole transaction, so a second concurrent approval
        // waits, then sees APPROVED here and is rejected; stock rows are locked the same way in GetForStoreProductAsync.
        await _returns.InTransactionAsync(async () =>
        {
        var rma = await _returns.GetWithLinesForUpdateAsync(id) ?? throw new NotFoundException($"Return {id} was not found.");
        if (rma.Status != ReturnStatus.Draft)
            throw new BusinessRuleException($"A {rma.Status} return cannot be approved.");

        var user = await _users.GetAsync(userId) ?? throw new NotFoundException($"User {userId} was not found.");
        if (!user.IsActive) throw new BusinessRuleException("This user is not active.");

        // BR-06
        if (rma.RefundTotal > _options.ManagerApprovalThreshold && user.Role != "MANAGER" && user.Role != "ADMIN")
            throw new BusinessRuleException($"A refund above {_options.ManagerApprovalThreshold:0.00} needs approval from a manager or admin.");

        var order = await _orders.GetWithDetailsAsync(rma.OrderId) ?? throw new NotFoundException($"Order {rma.OrderId} was not found.");
        var now = NowUtc;

        // BR-07: restock A and B at the receiving store; SCRAP goes to the supplier claim pile.
        foreach (var line in rma.Lines)
        {
            if (ReturnGrade.IsRestockable(line.Grade) && !line.Restocked)
            {
                var productId = order.Lines.First(x => x.Id == line.OrderLineId).ProductId;
                var stock = await _stock.GetForStoreProductAsync(rma.StoreId, productId);
                if (stock == null)
                {
                    stock = new StockLevel { StoreId = rma.StoreId, ProductId = productId, Quantity = 0, ReorderLevel = 0 };
                    await _stock.AddAsync(stock);
                }
                stock.Quantity += line.Quantity;
                stock.UpdatedUtc = now;
                line.Restocked = true;
            }
            if (line.Grade == ReturnGrade.Scrap) line.SupplierClaim = true;
        }

        rma.Status = ReturnStatus.Approved;
        rma.ApprovedByUserId = userId;
        rma.ApprovedUtc = now;
        await _returns.SaveAsync(); // status, lines and stock commit together with the lock release
        return rma.Id;
        });
        return await GetAsync(id);
    }

    public async Task<ReturnDto> RejectAsync(int id, int userId)
    {
        await _approveValidator.ValidateAndThrowAsync(new ApproveReturnDto(userId));
        await _returns.InTransactionAsync(async () =>
        {
            var rma = await _returns.GetWithLinesForUpdateAsync(id) ?? throw new NotFoundException($"Return {id} was not found.");
            if (rma.Status != ReturnStatus.Draft)
                throw new BusinessRuleException($"A {rma.Status} return cannot be rejected.");
            var user = await _users.GetAsync(userId) ?? throw new NotFoundException($"User {userId} was not found.");
            if (!user.IsActive) throw new BusinessRuleException("This user is not active.");
            rma.Status = ReturnStatus.Rejected; // frees the quantity and refund cap (REJECTED RMAs do not count, BR-03/BR-05)
            await _returns.SaveAsync();
            return rma.Id;
        });
        return await GetAsync(id);
    }

    // BR-08: advisory only, counts APPROVED RMAs only (DRAFTs are unconfirmed); callers must never block a refund on it.
    public async Task<bool> IsSerialReturnerAsync(int customerId) => (await GetSerialStatsAsync(customerId)).Flagged;

    private sealed record SerialStats(bool Flagged, int RmaCount, int DistinctOrders, decimal ReturnedValue, decimal PurchasedValue, decimal RatePct);

    private async Task<SerialStats> GetSerialStatsAsync(int customerId)
    {
        var since = NowUtc.AddDays(-_options.SerialWindowDays);
        var rmas = (await _returns.ListForCustomerSinceAsync(customerId, since)).Where(r => r.Status == ReturnStatus.Approved).ToList();
        if (rmas.Count == 0) return new SerialStats(false, 0, 0, 0m, 0m, 0m);
        var returned = rmas.Sum(r => r.RefundTotal);
        var purchased = await _returns.SumPurchasedSinceAsync(customerId, since);
        var rate = purchased > 0 ? Math.Round(returned * 100m / purchased, 2) : 0m;
        var orders = rmas.Select(r => r.OrderId).Distinct().Count();
        var flagged = rmas.Count >= _options.SerialMinRmas && orders >= _options.SerialMinOrders && rate >= _options.SerialMinRatePercent;
        return new SerialStats(flagged, rmas.Count, orders, returned, purchased, rate);
    }

    public async Task<ReturnsDashboardDto> GetDashboardAsync(DateOnly from, DateOnly to)
    {
        if (to < from) throw new BusinessRuleException("The end date is before the start date.");
        var start = from.ToDateTime(TimeOnly.MinValue);
        var end = to.ToDateTime(TimeOnly.MinValue).AddDays(1);
        var sold = await _returns.GetSoldLinesAsync(start, end);
        var returned = await _returns.GetReturnedLinesAsync(start, end);

        static List<ReturnRateRowDto> Build(IEnumerable<(string Key, string Label, int Qty)> sold,
            IEnumerable<(string Key, string Label, int Qty, decimal Refund)> returned)
        {
            var soldBy = sold.GroupBy(s => s.Key).ToDictionary(g => g.Key, g => (g.First().Label, Qty: g.Sum(x => x.Qty)));
            var retBy = returned.GroupBy(r => r.Key).ToDictionary(g => g.Key, g => (g.First().Label, Qty: g.Sum(x => x.Qty), Refund: g.Sum(x => x.Refund)));
            return retBy.Select(kv =>
            {
                var sQty = soldBy.TryGetValue(kv.Key, out var s) ? s.Qty : 0;
                var pct = sQty > 0 ? Math.Round(kv.Value.Qty * 100m / sQty, 2) : 0m;
                return new ReturnRateRowDto(kv.Key, kv.Value.Label, sQty, kv.Value.Qty, pct, kv.Value.Refund);
            }).OrderByDescending(r => r.ReturnRatePct).ThenByDescending(r => r.ReturnedQuantity).ToList();
        }

        var byProduct = Build(sold.Select(s => (s.ProductId.ToString(), $"{s.Sku} {s.ProductName}", s.Quantity)),
            returned.Select(r => (r.ProductId.ToString(), $"{r.Sku} {r.ProductName}", r.Quantity, r.RefundAmount)));
        var byCategory = Build(sold.Select(s => (s.CategoryId.ToString(), s.CategoryName, s.Quantity)),
            returned.Select(r => (r.CategoryId.ToString(), r.CategoryName, r.Quantity, r.RefundAmount)));
        var byStore = Build(sold.Select(s => (s.StoreId.ToString(), s.StoreName, s.Quantity)),
            returned.Select(r => (r.StoreId.ToString(), r.StoreName, r.Quantity, r.RefundAmount)));

        var flagged = new List<FlaggedCustomerDto>();
        foreach (var customerId in await _returns.ListCustomerIdsWithReturnsSinceAsync(NowUtc.AddDays(-_options.SerialWindowDays)))
        {
            var stats = await GetSerialStatsAsync(customerId);
            if (!stats.Flagged) continue;
            var c = await _customers.GetReadOnlyAsync(customerId);
            flagged.Add(new FlaggedCustomerDto(customerId, c == null ? "" : $"{c.FirstName} {c.LastName}",
                stats.RmaCount, stats.DistinctOrders, stats.ReturnedValue, stats.PurchasedValue, stats.RatePct));
        }

        return new ReturnsDashboardDto(from, to, byProduct, byCategory, byStore, flagged.OrderByDescending(f => f.RatePct).ToList());
    }
}
