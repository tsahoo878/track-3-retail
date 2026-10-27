using System.Globalization;
using FluentValidation;
using Kestrel.Api.Domain;
using Kestrel.Api.Dtos;
using Kestrel.Api.Infrastructure;
using Kestrel.Api.Repositories;
using Kestrel.Api.Services;
using Kestrel.Api.Validators;
using Microsoft.Extensions.Options;
using Moq;

namespace Kestrel.Tests;

public class FakeClock : TimeProvider
{
    private readonly DateTimeOffset _now;
    public FakeClock(DateTimeOffset now) => _now = now;
    public override DateTimeOffset GetUtcNow() => _now;
}

// In-memory stand-in for the RMA tables, so several RMAs on the same order see each other.
public class InMemoryReturnRepository : IReturnRepository
{
    public List<Return> Items { get; } = new();
    public decimal PurchasedValue { get; set; }
    private int _nextId = 100;

    public Task<Return?> GetWithLinesAsync(int id) => Task.FromResult(Items.FirstOrDefault(r => r.Id == id));
    public Task<Return?> GetWithLinesForUpdateAsync(int id) => GetWithLinesAsync(id);
    public Task<List<Return>> ListForOrderAsync(int orderId) => Task.FromResult(Items.Where(r => r.OrderId == orderId).ToList());
    public Task<List<Return>> ListForCustomerSinceAsync(int customerId, DateTime sinceUtc) =>
        Task.FromResult(Items.Where(r => r.CustomerId == customerId && r.CreatedUtc >= sinceUtc).ToList());
    public Task<decimal> SumPurchasedSinceAsync(int customerId, DateTime sinceUtc) => Task.FromResult(PurchasedValue);
    public Task<List<int>> ListCustomerIdsWithReturnsSinceAsync(DateTime sinceUtc) =>
        Task.FromResult(Items.Where(r => r.CreatedUtc >= sinceUtc).Select(r => r.CustomerId).Distinct().ToList());
    public Task<bool> RmaNumberExistsAsync(string rmaNumber) => Task.FromResult(Items.Any(r => r.RmaNumber == rmaNumber));
    public Task<List<SoldLineRow>> GetSoldLinesAsync(DateTime fromUtcInclusive, DateTime toUtcExclusive) => Task.FromResult(new List<SoldLineRow>());
    public Task<List<ReturnedLineRow>> GetReturnedLinesAsync(DateTime fromUtcInclusive, DateTime toUtcExclusive) => Task.FromResult(new List<ReturnedLineRow>());
    public Task AddAsync(Return rma)
    {
        rma.Id = _nextId++;
        var lineId = rma.Id * 10;
        foreach (var l in rma.Lines) l.Id = lineId++;
        Items.Add(rma);
        return Task.CompletedTask;
    }
    public Task SaveAsync() => Task.CompletedTask;

    // Mimics a database transaction: if the work throws, every RMA status and the item list go back to what they were.
    public async Task<T> InTransactionAsync<T>(Func<Task<T>> work)
    {
        var items = Items.ToList();
        var statuses = items.ToDictionary(r => r, r => r.Status);
        var nextId = _nextId;
        try { return await work(); }
        catch
        {
            Items.Clear();
            Items.AddRange(items);
            foreach (var kv in statuses) kv.Key.Status = kv.Value;
            _nextId = nextId;
            throw;
        }
    }
}

public class ReturnServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);

    private readonly InMemoryReturnRepository _returns = new();
    private readonly Mock<IOrderRepository> _orders = new();
    private readonly Mock<ICustomerRepository> _customers = new();
    private readonly Mock<IStoreRepository> _stores = new();
    private readonly Mock<ICategoryRepository> _categories = new();
    private readonly Mock<IStockRepository> _stock = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Dictionary<(int Store, int Product), StockLevel> _stockLevels = new();
    private readonly ReturnsOptions _options = new();
    private readonly ReturnService _sut;

    private const int Staff = 1, Manager = 2, Admin = 3;

    private static readonly Dictionary<string, Category> Categories = new()
    {
        ["TENT"] = new Category { Id = 1, Code = "TENT", ReturnWindowDays = 30 },
        ["BOOT"] = new Category { Id = 2, Code = "BOOT", ReturnWindowDays = 60 },
        ["JKT-WP"] = new Category { Id = 3, Code = "JKT-WP", ReturnWindowDays = 60 },
        ["JKT-INS"] = new Category { Id = 4, Code = "JKT-INS", ReturnWindowDays = 60 },
    };

    public ReturnServiceTests()
    {
        _sut = new ReturnService(_returns, _orders.Object, _customers.Object, _stores.Object, _categories.Object,
            _stock.Object, _users.Object, new CreateReturnValidator(), new ApproveReturnValidator(),
            Options.Create(_options), new FakeClock(Now));

        _customers.Setup(r => r.GetReadOnlyAsync(It.IsAny<int>()))
            .ReturnsAsync(new Customer { Id = 7, FirstName = "Ada", LastName = "Walker", LoyaltyTier = "NONE" });
        _stores.Setup(r => r.GetAsync(It.IsAny<int>()))
            .ReturnsAsync((int id) => new Store { Id = id, Name = $"Store {id}", IsActive = true });
        _users.Setup(r => r.GetAsync(Staff)).ReturnsAsync(new AppUser { Id = Staff, Role = "STAFF", IsActive = true });
        _users.Setup(r => r.GetAsync(Manager)).ReturnsAsync(new AppUser { Id = Manager, Role = "MANAGER", IsActive = true });
        _users.Setup(r => r.GetAsync(Admin)).ReturnsAsync(new AppUser { Id = Admin, Role = "ADMIN", IsActive = true });
        _users.Setup(r => r.GetAsync(4)).ReturnsAsync(new AppUser { Id = 4, Role = "STAFF", IsActive = true });

        _categories.Setup(r => r.GetAsync(It.IsAny<int>())).ReturnsAsync((int id) => Categories.Values.FirstOrDefault(c => c.Id == id));
        _stock.Setup(r => r.GetForStoreProductAsync(It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync((int s, int p) => _stockLevels.GetValueOrDefault((s, p)));
        _stock.Setup(r => r.AddAsync(It.IsAny<StockLevel>()))
            .Callback<StockLevel>(s => _stockLevels[(s.StoreId, s.ProductId)] = s).Returns(Task.CompletedTask);
    }

    // Builds an order and registers it with the mocks. The payment equals the total unless overridden.
    private Order GivenOrder(int id = 1, int daysAgo = 5, string status = OrderStatus.Paid, decimal discount = 0m,
        decimal? captured = null, DateTime? orderedUtc = null, int storeId = 1,
        params (string Category, int Qty, decimal UnitPrice)[] lines)
    {
        if (lines.Length == 0) lines = new[] { ("TENT", 1, 100m) };
        var order = new Order
        {
            Id = id, OrderNumber = $"KO-{id:0000}", CustomerId = 7, StoreId = storeId, Status = status,
            OrderedUtc = orderedUtc ?? Now.UtcDateTime.AddDays(-daysAgo), DiscountAmount = discount
        };
        var lineId = id * 1000;
        foreach (var l in lines)
        {
            lineId++;
            order.Lines.Add(new OrderLine
            {
                Id = lineId, OrderId = id, ProductId = lineId, Quantity = l.Qty, UnitPrice = l.UnitPrice, LineTotal = l.Qty * l.UnitPrice,
                Product = new Product { Id = lineId, Sku = $"SKU-{lineId}", Name = $"Product {lineId}", CategoryId = Categories[l.Category].Id }
            });
        }
        order.Subtotal = order.Lines.Sum(x => x.LineTotal);
        order.TotalAmount = order.Subtotal - discount;
        order.Payments.Add(new Payment { OrderId = id, Amount = captured ?? order.TotalAmount, Status = "CAPTURED" });
        _orders.Setup(r => r.GetWithDetailsByNumberAsync(order.OrderNumber)).ReturnsAsync(order);
        _orders.Setup(r => r.GetWithDetailsAsync(id)).ReturnsAsync(order);
        return order;
    }

    private static CreateReturnDto RmaAt(Order order, int storeId, params (int LineIndex, int Qty, string Grade)[] lines) =>
        new(order.OrderNumber, storeId, 4, lines.Select(l => new ReturnLineInputDto(order.Lines[l.LineIndex].Id, l.Qty, l.Grade)).ToList());

    private static CreateReturnDto Rma(Order order, params (int LineIndex, int Qty, string Grade)[] lines) => RmaAt(order, 1, lines);

    // ---- BR-01 return window -------------------------------------------------------------------

    [Theory]
    [InlineData("TENT", 29, true)]
    [InlineData("TENT", 30, true)]
    [InlineData("TENT", 31, false)]
    [InlineData("BOOT", 31, true)]
    [InlineData("BOOT", 60, true)]
    [InlineData("BOOT", 61, false)]
    [InlineData("JKT-WP", 60, true)]
    [InlineData("JKT-WP", 61, false)]
    [InlineData("JKT-INS", 60, true)]
    [InlineData("JKT-INS", 61, false)]
    public async Task The_return_window_is_inclusive_and_depends_on_the_category(string category, int daysAgo, bool allowed)
    {
        var order = GivenOrder(daysAgo: daysAgo, lines: new[] { (category, 1, 100m) });

        if (allowed)
        {
            var result = await _sut.CreateAsync(Rma(order, (0, 1, "A")));
            Assert.Equal(ReturnStatus.Draft, result.Status);
        }
        else
        {
            var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => _sut.CreateAsync(Rma(order, (0, 1, "A"))));
            Assert.Contains("return window", ex.Message);
            Assert.Empty(_returns.Items);
        }
    }

    [Fact]
    public async Task The_window_counts_calendar_days_not_hours()
    {
        // Ordered 23:59 on 27 Sep: the 30th day is 27 Oct, so a return at 12:00 on 27 Oct is fine.
        var inside = GivenOrder(id: 1, orderedUtc: new DateTime(2026, 9, 27, 23, 59, 0, DateTimeKind.Utc));
        await _sut.CreateAsync(Rma(inside, (0, 1, "A")));

        var outside = GivenOrder(id: 2, orderedUtc: new DateTime(2026, 9, 26, 23, 59, 0, DateTimeKind.Utc));
        await Assert.ThrowsAsync<BusinessRuleException>(() => _sut.CreateAsync(Rma(outside, (0, 1, "A"))));
    }

    [Fact]
    public async Task Only_the_expired_line_is_refused_when_categories_differ()
    {
        var order = GivenOrder(daysAgo: 45, lines: new[] { ("BOOT", 1, 100m), ("TENT", 1, 100m) });

        await _sut.CreateAsync(Rma(order, (0, 1, "A")));
        await Assert.ThrowsAsync<BusinessRuleException>(() => _sut.CreateAsync(Rma(order, (1, 1, "A"))));
    }

    [Fact]
    public async Task Eligibility_reports_returnableUntil_per_line()
    {
        var order = GivenOrder(daysAgo: 10, lines: new[] { ("TENT", 1, 100m), ("BOOT", 1, 100m), ("JKT-WP", 1, 100m) });

        var result = await _sut.GetEligibilityAsync(order.OrderNumber);

        var ordered = DateOnly.FromDateTime(order.OrderedUtc);
        Assert.Equal(ordered.AddDays(30), result.Lines[0].ReturnableUntil);
        Assert.Equal(ordered.AddDays(60), result.Lines[1].ReturnableUntil);
        Assert.Equal(ordered.AddDays(60), result.Lines[2].ReturnableUntil);
        Assert.Equal(ordered.AddDays(60), result.ReturnableUntil);
        Assert.True(result.Eligible);
        Assert.All(result.Lines, l => Assert.True(l.Eligible));
    }

    [Fact]
    public async Task Eligibility_marks_only_the_expired_line_as_not_returnable()
    {
        var order = GivenOrder(daysAgo: 45, lines: new[] { ("TENT", 1, 100m), ("BOOT", 1, 100m) });

        var result = await _sut.GetEligibilityAsync(order.OrderNumber);

        Assert.False(result.Lines[0].Eligible);
        Assert.Equal(0, result.Lines[0].ReturnableQuantity);
        Assert.Contains("30-day", result.Lines[0].Reason);
        Assert.True(result.Lines[1].Eligible);
        Assert.True(result.Eligible);
    }

    [Fact]
    public async Task Eligibility_of_an_unknown_order_is_not_found()
    {
        _orders.Setup(r => r.GetWithDetailsByNumberAsync("NOPE")).ReturnsAsync((Order?)null);
        await Assert.ThrowsAsync<NotFoundException>(() => _sut.GetEligibilityAsync("NOPE"));
    }

    // ---- BR-02 order status --------------------------------------------------------------------

    [Theory]
    [InlineData(OrderStatus.Cancelled)]
    [InlineData(OrderStatus.Placed)]
    public async Task Cancelled_and_placed_orders_cannot_be_returned(string status)
    {
        var order = GivenOrder(status: status);

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => _sut.CreateAsync(Rma(order, (0, 1, "A"))));
        Assert.Contains(status, ex.Message);

        var eligibility = await _sut.GetEligibilityAsync(order.OrderNumber);
        Assert.False(eligibility.Eligible);
        Assert.NotNull(eligibility.Reason);
        Assert.All(eligibility.Lines, l => Assert.False(l.Eligible));
    }

    [Theory]
    [InlineData(OrderStatus.Paid)]
    [InlineData(OrderStatus.Fulfilled)]
    public async Task Paid_and_fulfilled_orders_can_be_returned(string status)
    {
        var order = GivenOrder(status: status);
        var result = await _sut.CreateAsync(Rma(order, (0, 1, "A")));
        Assert.Equal(100m, result.RefundTotal);
    }

    // ---- BR-03 quantity ------------------------------------------------------------------------

    [Fact]
    public async Task Cannot_return_more_than_was_ordered_in_one_rma()
    {
        var order = GivenOrder(lines: new[] { ("TENT", 2, 50m) });
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => _sut.CreateAsync(Rma(order, (0, 3, "A"))));
        Assert.Contains("only 2 of 2", ex.Message);
    }

    // Approves with a manager so refunds above the staff threshold are fine; approved RMAs keep counting against the caps.
    private async Task<ReturnDto> CreateAndApprove(Order order, params (int LineIndex, int Qty, string Grade)[] lines)
    {
        var draft = await _sut.CreateAsync(Rma(order, lines));
        return await _sut.ApproveAsync(draft.Id, Manager);
    }

    [Fact]
    public async Task Quantity_is_counted_across_every_rma_on_the_order()
    {
        var order = GivenOrder(lines: new[] { ("TENT", 3, 30m) });

        await CreateAndApprove(order, (0, 1, "A"));
        await CreateAndApprove(order, (0, 1, "B"));
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => _sut.CreateAsync(Rma(order, (0, 2, "A"))));
        Assert.Contains("only 1 of 3", ex.Message);

        await CreateAndApprove(order, (0, 1, "A")); // the last unit is still allowed
        await Assert.ThrowsAsync<BusinessRuleException>(() => _sut.CreateAsync(Rma(order, (0, 1, "A"))));
    }

    [Fact]
    public async Task A_rejected_rma_does_not_use_up_the_quantity()
    {
        var order = GivenOrder(lines: new[] { ("TENT", 1, 30m) });
        var first = await _sut.CreateAsync(Rma(order, (0, 1, "A")));
        _returns.Items.Single(r => r.Id == first.Id).Status = ReturnStatus.Rejected;

        var second = await _sut.CreateAsync(Rma(order, (0, 1, "A")));
        Assert.Equal(30m, second.RefundTotal);
    }

    [Fact]
    public async Task A_line_from_another_order_is_refused()
    {
        var order = GivenOrder();
        var dto = new CreateReturnDto(order.OrderNumber, 1, 4, new List<ReturnLineInputDto> { new(999999, 1, "A") });
        await Assert.ThrowsAsync<BusinessRuleException>(() => _sut.CreateAsync(dto));
    }

    [Theory]
    [InlineData("X")]
    [InlineData("")]
    public async Task An_unknown_grade_is_a_validation_error(string grade)
    {
        var order = GivenOrder();
        await Assert.ThrowsAsync<ValidationException>(() => _sut.CreateAsync(Rma(order, (0, 1, grade))));
    }

    [Fact]
    public async Task Grades_are_accepted_in_any_case()
    {
        var order = GivenOrder();
        var result = await _sut.CreateAsync(Rma(order, (0, 1, " scrap ")));
        Assert.Equal("SCRAP", result.Lines.Single().Grade);
    }

    // ---- BR-04 / BR-05 refund caps -------------------------------------------------------------

    [Fact]
    public async Task A_grade_c_item_refunds_85_percent()
    {
        var order = GivenOrder(lines: new[] { ("TENT", 1, 100m) });
        var result = await _sut.CreateAsync(Rma(order, (0, 1, "C")));
        Assert.Equal(85m, result.RefundTotal);
    }

    [Fact]
    public async Task The_refund_uses_the_discounted_price()
    {
        // 100.00 line, 20.00 discount on the order: only 80.00 was paid.
        var order = GivenOrder(discount: 20m, lines: new[] { ("TENT", 1, 100m) });
        var result = await _sut.CreateAsync(Rma(order, (0, 1, "A")));
        Assert.Equal(80m, result.RefundTotal);
    }

    [Fact]
    public async Task Refunds_on_a_discounted_order_never_exceed_what_was_paid_even_over_repeated_rmas()
    {
        // 3 x 33.33 = 99.99 and 1 x 10.00, 11.00 off: 98.99 paid. Return everything, one unit at a time.
        var order = GivenOrder(discount: 11m, lines: new[] { ("TENT", 3, 33.33m), ("BOOT", 1, 10m) });
        var paid = ReturnPricing.PaidPerLine(order);

        var refunded = new Dictionary<int, decimal> { [order.Lines[0].Id] = 0m, [order.Lines[1].Id] = 0m };
        foreach (var (lineIndex, grade) in new[] { (0, "A"), (0, "B"), (1, "A"), (0, "SCRAP") })
        {
            var rma = await CreateAndApprove(order, (lineIndex, 1, grade));
            refunded[order.Lines[lineIndex].Id] += rma.RefundTotal;
            foreach (var l in order.Lines)
                Assert.True(refunded[l.Id] <= paid[l.Id], $"line {l.Id} refunded {refunded[l.Id]} of {paid[l.Id]}");
        }

        Assert.Equal(98.99m, refunded.Values.Sum());
        Assert.Equal(paid[order.Lines[0].Id], refunded[order.Lines[0].Id]);
        Assert.Equal(paid[order.Lines[1].Id], refunded[order.Lines[1].Id]);

        // Nothing is left to return, so another RMA is refused.
        await Assert.ThrowsAsync<BusinessRuleException>(() => _sut.CreateAsync(Rma(order, (0, 1, "A"))));
    }

    [Fact]
    public async Task Eligibility_shows_what_can_still_be_refunded_after_earlier_rmas()
    {
        var order = GivenOrder(lines: new[] { ("TENT", 4, 25m) });
        await CreateAndApprove(order, (0, 1, "A"));
        await CreateAndApprove(order, (0, 1, "C"));

        var line = (await _sut.GetEligibilityAsync(order.OrderNumber)).Lines.Single();

        Assert.Equal(2, line.QuantityReturned);
        Assert.Equal(2, line.ReturnableQuantity);
        Assert.Equal(100m, line.PaidAmount);
        Assert.Equal(25m + 21.25m, line.RefundedAmount);
        Assert.Equal(100m - 46.25m, line.ReturnableAmount);
    }

    [Fact]
    public async Task The_order_total_refunded_never_exceeds_the_captured_payments()
    {
        // The customer was only charged 60.00 although the lines add up to 100.00 (for example a part-failed capture).
        var order = GivenOrder(captured: 60m, lines: new[] { ("TENT", 1, 100m) });

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => _sut.CreateAsync(Rma(order, (0, 1, "A"))));
        Assert.Contains("more than the customer paid", ex.Message);
        Assert.Empty(_returns.Items);
    }

    [Fact]
    public async Task Only_captured_payments_count_towards_the_order_cap()
    {
        var order = GivenOrder(lines: new[] { ("TENT", 1, 100m) });
        order.Payments.Clear();
        order.Payments.Add(new Payment { Amount = 100m, Status = "FAILED" });

        await Assert.ThrowsAsync<BusinessRuleException>(() => _sut.CreateAsync(Rma(order, (0, 1, "A"))));
    }

    [Fact]
    public async Task Eligibility_never_offers_more_than_the_order_has_left()
    {
        var order = GivenOrder(captured: 60m, lines: new[] { ("TENT", 1, 100m) });
        var result = await _sut.GetEligibilityAsync(order.OrderNumber);
        Assert.Equal(60m, result.Lines.Single().ReturnableAmount);
    }

    // ---- BR-06 approval threshold --------------------------------------------------------------

    private async Task<ReturnDto> DraftFor(decimal amount, int orderId = 1)
    {
        var order = GivenOrder(id: orderId, lines: new[] { ("TENT", 1, amount) });
        return await _sut.CreateAsync(Rma(order, (0, 1, "A")));
    }

    [Theory]
    [InlineData("150.00")]
    [InlineData("149.99")]
    [InlineData("20.00")]
    public async Task Staff_can_approve_up_to_and_including_150(string amountText)
    {
        var draft = await DraftFor(decimal.Parse(amountText, CultureInfo.InvariantCulture));
        Assert.False(draft.RequiresManagerApproval);

        var result = await _sut.ApproveAsync(draft.Id, Staff);

        Assert.Equal(ReturnStatus.Approved, result.Status);
        Assert.Equal(Staff, result.ApprovedByUserId);
        Assert.NotNull(result.ApprovedUtc);
    }

    [Theory]
    [InlineData("150.01")]
    [InlineData("151.00")]
    [InlineData("400.00")]
    public async Task Staff_cannot_approve_above_150(string amountText)
    {
        var draft = await DraftFor(decimal.Parse(amountText, CultureInfo.InvariantCulture));
        Assert.True(draft.RequiresManagerApproval);

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => _sut.ApproveAsync(draft.Id, Staff));
        Assert.Contains("manager", ex.Message);

        var stored = _returns.Items.Single();
        Assert.Equal(ReturnStatus.Draft, stored.Status);
        Assert.Null(stored.ApprovedByUserId);
        Assert.All(stored.Lines, l => Assert.False(l.Restocked));
        Assert.Empty(_stockLevels);
    }

    [Theory]
    [InlineData(Manager)]
    [InlineData(Admin)]
    public async Task Managers_and_admins_can_approve_above_150(int userId)
    {
        var draft = await DraftFor(400m);
        var result = await _sut.ApproveAsync(draft.Id, userId);
        Assert.Equal(ReturnStatus.Approved, result.Status);
        Assert.Equal(userId, result.ApprovedByUserId);
    }

    [Fact]
    public async Task An_unknown_or_inactive_approver_is_refused()
    {
        var draft = await DraftFor(10m);
        _users.Setup(r => r.GetAsync(50)).ReturnsAsync((AppUser?)null);
        _users.Setup(r => r.GetAsync(51)).ReturnsAsync(new AppUser { Id = 51, Role = "MANAGER", IsActive = false });

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.ApproveAsync(draft.Id, 50));
        await Assert.ThrowsAsync<BusinessRuleException>(() => _sut.ApproveAsync(draft.Id, 51));
    }

    [Fact]
    public async Task Approving_twice_is_rejected_and_stock_is_only_added_once()
    {
        var order = GivenOrder(lines: new[] { ("TENT", 2, 20m) });
        var draft = await _sut.CreateAsync(Rma(order, (0, 2, "A")));

        await _sut.ApproveAsync(draft.Id, Staff);
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => _sut.ApproveAsync(draft.Id, Manager));

        Assert.Contains("APPROVED", ex.Message);
        Assert.Equal(2, _stockLevels.Single().Value.Quantity);
    }

    [Fact]
    public async Task A_rejected_rma_cannot_be_approved()
    {
        var draft = await DraftFor(10m);
        _returns.Items.Single().Status = ReturnStatus.Rejected;
        await Assert.ThrowsAsync<BusinessRuleException>(() => _sut.ApproveAsync(draft.Id, Manager));
    }

    [Fact]
    public async Task Approving_an_unknown_rma_is_not_found()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => _sut.ApproveAsync(12345, Manager));
    }

    // ---- BR-07 restock and supplier claim ------------------------------------------------------

    [Fact]
    public async Task Grades_a_and_b_are_restocked_at_the_rma_store_and_c_and_scrap_are_not()
    {
        // The order was placed at store 1, the goods come back to store 2.
        var order = GivenOrder(storeId: 1, lines: new[] { ("TENT", 1, 10m), ("TENT", 2, 10m), ("TENT", 3, 10m), ("TENT", 4, 10m) });
        var p = order.Lines.Select(l => l.ProductId).ToList();
        _stockLevels[(2, p[0])] = new StockLevel { Id = 1, StoreId = 2, ProductId = p[0], Quantity = 5 };
        _stockLevels[(2, p[1])] = new StockLevel { Id = 2, StoreId = 2, ProductId = p[1], Quantity = 0 };
        _stockLevels[(2, p[2])] = new StockLevel { Id = 3, StoreId = 2, ProductId = p[2], Quantity = 7 };
        _stockLevels[(2, p[3])] = new StockLevel { Id = 4, StoreId = 2, ProductId = p[3], Quantity = 9 };
        var orderStoreStock = new StockLevel { Id = 9, StoreId = 1, ProductId = p[0], Quantity = 100 };
        _stockLevels[(1, p[0])] = orderStoreStock;

        var draft = await _sut.CreateAsync(RmaAt(order, 2, (0, 1, "A"), (1, 2, "B"), (2, 3, "C"), (3, 4, "SCRAP")));
        var result = await _sut.ApproveAsync(draft.Id, Staff);

        Assert.Equal(6, _stockLevels[(2, p[0])].Quantity);   // A: +1
        Assert.Equal(2, _stockLevels[(2, p[1])].Quantity);   // B: +2
        Assert.Equal(7, _stockLevels[(2, p[2])].Quantity);   // C: unchanged
        Assert.Equal(9, _stockLevels[(2, p[3])].Quantity);   // SCRAP: unchanged
        Assert.Equal(100, orderStoreStock.Quantity);          // the order's own store is untouched
        Assert.Equal(new[] { true, true, false, false }, result.Lines.Select(l => l.Restocked).ToArray());
        Assert.Equal(new[] { false, false, false, true }, result.Lines.Select(l => l.SupplierClaim).ToArray());
        Assert.Equal(Now.UtcDateTime, _stockLevels[(2, p[0])].UpdatedUtc);
    }

    [Fact]
    public async Task Restocking_creates_the_stock_row_when_the_store_has_none()
    {
        var order = GivenOrder(lines: new[] { ("TENT", 2, 10m) });
        var draft = await _sut.CreateAsync(RmaAt(order, 3, (0, 2, "A")));

        await _sut.ApproveAsync(draft.Id, Staff);

        Assert.Equal(2, _stockLevels[(3, order.Lines[0].ProductId)].Quantity);
        _stock.Verify(r => r.AddAsync(It.Is<StockLevel>(s => s.StoreId == 3)), Times.Once);
    }

    [Fact]
    public async Task Scrap_sets_the_supplier_claim_and_no_other_grade_does()
    {
        var order = GivenOrder(lines: new[] { ("TENT", 1, 10m), ("TENT", 1, 10m), ("TENT", 1, 10m), ("TENT", 1, 10m) });
        var draft = await _sut.CreateAsync(Rma(order, (0, 1, "A"), (1, 1, "B"), (2, 1, "C"), (3, 1, "SCRAP")));

        var result = await _sut.ApproveAsync(draft.Id, Staff);

        Assert.Equal("SCRAP", result.Lines.Single(l => l.SupplierClaim).Grade);
        Assert.False(result.Lines.Single(l => l.Grade == "SCRAP").Restocked);
        Assert.DoesNotContain(_stockLevels.Keys, k => k.Product == order.Lines[3].ProductId);
    }

    [Fact]
    public async Task Creating_a_draft_does_not_touch_stock()
    {
        var order = GivenOrder();
        await _sut.CreateAsync(Rma(order, (0, 1, "A")));
        _stock.Verify(r => r.AddAsync(It.IsAny<StockLevel>()), Times.Never);
        Assert.Empty(_stockLevels);
    }


    // ---- Reject, supersede, transactions -------------------------------------------------------

    [Fact]
    public async Task Rejecting_a_draft_frees_the_quantity_and_the_refund_cap()
    {
        var order = GivenOrder(lines: new[] { ("TENT", 2, 50m) });
        var draft = await _sut.CreateAsync(Rma(order, (0, 2, "A")));
        Assert.Equal(0, (await _sut.GetEligibilityAsync(order.OrderNumber)).Lines.Single().ReturnableQuantity);

        var rejected = await _sut.RejectAsync(draft.Id, Staff);

        Assert.Equal(ReturnStatus.Rejected, rejected.Status);
        var line = (await _sut.GetEligibilityAsync(order.OrderNumber)).Lines.Single();
        Assert.Equal(2, line.ReturnableQuantity);
        Assert.Equal(100m, line.ReturnableAmount);
        Assert.Equal(0m, line.RefundedAmount);
        var again = await _sut.CreateAsync(Rma(order, (0, 2, "A")));
        Assert.Equal(100m, again.RefundTotal);
        Assert.Empty(_stockLevels);
    }

    [Theory]
    [InlineData(ReturnStatus.Approved)]
    [InlineData(ReturnStatus.Rejected)]
    public async Task Only_a_draft_can_be_rejected(string status)
    {
        var draft = await DraftFor(10m);
        _returns.Items.Single().Status = status;

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => _sut.RejectAsync(draft.Id, Manager));
        Assert.Contains(status, ex.Message);
        Assert.Equal(status, _returns.Items.Single().Status);
    }

    [Fact]
    public async Task Rejecting_an_unknown_rma_is_not_found()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => _sut.RejectAsync(12345, Manager));
    }

    [Fact]
    public async Task An_unknown_or_inactive_user_cannot_reject()
    {
        var draft = await DraftFor(10m);
        _users.Setup(r => r.GetAsync(50)).ReturnsAsync((AppUser?)null);
        _users.Setup(r => r.GetAsync(51)).ReturnsAsync(new AppUser { Id = 51, Role = "MANAGER", IsActive = false });

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.RejectAsync(draft.Id, 50));
        await Assert.ThrowsAsync<BusinessRuleException>(() => _sut.RejectAsync(draft.Id, 51));
        Assert.Equal(ReturnStatus.Draft, _returns.Items.Single().Status);
    }

    [Fact]
    public async Task A_new_draft_supersedes_the_earlier_draft_on_the_same_order()
    {
        var order = GivenOrder(lines: new[] { ("TENT", 2, 50m) });
        var first = await _sut.CreateAsync(Rma(order, (0, 1, "A")));
        var second = await _sut.CreateAsync(Rma(order, (0, 2, "A"))); // would not fit if the first still held a unit

        Assert.Equal(ReturnStatus.Rejected, _returns.Items.Single(r => r.Id == first.Id).Status);
        Assert.Equal(ReturnStatus.Draft, _returns.Items.Single(r => r.Id == second.Id).Status);
        Assert.Single(_returns.Items, r => r.Status == ReturnStatus.Draft);
    }

    [Fact]
    public async Task Superseding_leaves_approved_rmas_and_other_orders_alone()
    {
        var order = GivenOrder(id: 1, lines: new[] { ("TENT", 3, 20m) });
        var other = GivenOrder(id: 2, lines: new[] { ("TENT", 1, 20m) });
        var approved = await CreateAndApprove(order, (0, 1, "A"));
        var otherDraft = await _sut.CreateAsync(Rma(other, (0, 1, "A")));

        await _sut.CreateAsync(Rma(order, (0, 1, "A")));

        Assert.Equal(ReturnStatus.Approved, _returns.Items.Single(r => r.Id == approved.Id).Status);
        Assert.Equal(ReturnStatus.Draft, _returns.Items.Single(r => r.Id == otherDraft.Id).Status);
    }

    [Fact]
    public async Task A_superseded_rma_cannot_be_approved()
    {
        var order = GivenOrder(lines: new[] { ("TENT", 2, 20m) });
        var first = await _sut.CreateAsync(Rma(order, (0, 1, "A")));
        await _sut.CreateAsync(Rma(order, (0, 1, "A")));

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => _sut.ApproveAsync(first.Id, Manager));
        Assert.Contains("REJECTED", ex.Message);
        Assert.Empty(_stockLevels);
    }

    [Fact]
    public async Task A_rule_violation_in_create_propagates_and_persists_nothing()
    {
        var order = GivenOrder(lines: new[] { ("TENT", 2, 50m) });
        var draft = await _sut.CreateAsync(Rma(order, (0, 1, "A")));

        // Too many units: the earlier draft must not have been auto-rejected and no new RMA may exist.
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => _sut.CreateAsync(Rma(order, (0, 3, "A"))));
        Assert.Contains("only 2 of 2", ex.Message);

        var stored = Assert.Single(_returns.Items);
        Assert.Equal(draft.Id, stored.Id);
        Assert.Equal(ReturnStatus.Draft, stored.Status);
    }

    [Fact]
    public async Task A_second_approval_after_the_first_is_refused_and_stock_is_added_once()
    {
        var order = GivenOrder(lines: new[] { ("TENT", 3, 20m) });
        var draft = await _sut.CreateAsync(Rma(order, (0, 3, "A")));

        await _sut.ApproveAsync(draft.Id, Staff);
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => _sut.ApproveAsync(draft.Id, Staff));

        Assert.Contains("APPROVED", ex.Message);
        Assert.Equal(3, _stockLevels.Single().Value.Quantity);
        Assert.Equal(ReturnStatus.Approved, _returns.Items.Single().Status);
    }

    // ---- BR-08 serial returner -----------------------------------------------------------------

    private Return SeedRma(int orderId, decimal refund, int daysAgo = 10, string status = ReturnStatus.Approved)
    {
        var rma = new Return
        {
            Id = 900 + _returns.Items.Count, OrderId = orderId, CustomerId = 7, Status = status, RefundTotal = refund,
            CreatedUtc = Now.UtcDateTime.AddDays(-daysAgo), RmaNumber = $"RMA-SEED-{_returns.Items.Count}"
        };
        _returns.Items.Add(rma);
        return rma;
    }

    [Fact]
    public async Task One_order_with_several_returned_items_is_not_a_serial_returner()
    {
        _returns.PurchasedValue = 100m;
        SeedRma(1, 30m);
        SeedRma(1, 30m);
        SeedRma(1, 30m);
        SeedRma(1, 30m);

        Assert.False(await _sut.IsSerialReturnerAsync(7));
    }

    [Fact]
    public async Task Three_rmas_on_three_orders_at_40_percent_by_value_is_flagged()
    {
        _returns.PurchasedValue = 300m;
        SeedRma(1, 40m);
        SeedRma(2, 40m);
        SeedRma(3, 40m); // 120 of 300 = 40%

        Assert.True(await _sut.IsSerialReturnerAsync(7));
    }

    [Fact]
    public async Task Under_40_percent_by_value_is_not_flagged()
    {
        _returns.PurchasedValue = 300m;
        SeedRma(1, 40m);
        SeedRma(2, 40m);
        SeedRma(3, 39m); // 119 of 300 = 39.67%

        Assert.False(await _sut.IsSerialReturnerAsync(7));
    }

    [Fact]
    public async Task Two_orders_are_not_enough_even_with_three_rmas_and_a_high_rate()
    {
        _returns.PurchasedValue = 100m;
        SeedRma(1, 50m);
        SeedRma(1, 20m);
        SeedRma(2, 20m);

        Assert.False(await _sut.IsSerialReturnerAsync(7));
    }

    [Fact]
    public async Task Two_rmas_are_not_enough()
    {
        _returns.PurchasedValue = 100m;
        SeedRma(1, 50m);
        SeedRma(2, 50m);
        Assert.False(await _sut.IsSerialReturnerAsync(7));
    }

    [Fact]
    public async Task Rejected_and_old_rmas_do_not_count()
    {
        _returns.PurchasedValue = 100m;
        SeedRma(1, 30m);
        SeedRma(2, 30m);
        SeedRma(3, 30m, status: ReturnStatus.Rejected);
        Assert.False(await _sut.IsSerialReturnerAsync(7));

        SeedRma(3, 30m, daysAgo: 181);
        Assert.False(await _sut.IsSerialReturnerAsync(7));

        SeedRma(3, 30m, daysAgo: 179);
        Assert.True(await _sut.IsSerialReturnerAsync(7));
    }

    [Fact]
    public async Task The_thresholds_come_from_configuration()
    {
        _returns.PurchasedValue = 100m;
        SeedRma(1, 10m);
        SeedRma(2, 10m);
        SeedRma(3, 10m);
        Assert.False(await _sut.IsSerialReturnerAsync(7));

        _options.SerialMinRatePercent = 30m;
        Assert.True(await _sut.IsSerialReturnerAsync(7));

        _options.SerialMinOrders = 4;
        Assert.False(await _sut.IsSerialReturnerAsync(7));
    }

    [Fact]
    public async Task The_serial_flag_never_blocks_creating_or_approving_a_return()
    {
        _returns.PurchasedValue = 1000m;
        SeedRma(1, 400m);
        SeedRma(2, 400m);
        SeedRma(3, 400m);
        Assert.True(await _sut.IsSerialReturnerAsync(7));

        var order = GivenOrder(id: 9, lines: new[] { ("TENT", 1, 50m) });
        var draft = await _sut.CreateAsync(Rma(order, (0, 1, "A")));
        Assert.True(draft.SerialReturner);

        var approved = await _sut.ApproveAsync(draft.Id, Staff);
        Assert.Equal(ReturnStatus.Approved, approved.Status);
        Assert.True(approved.SerialReturner);

        Assert.True((await _sut.GetEligibilityAsync(order.OrderNumber)).SerialReturner);
    }

    [Fact]
    public async Task Draft_rmas_do_not_count_towards_the_serial_returner_flag()
    {
        _returns.PurchasedValue = 300m;
        SeedRma(1, 40m, status: ReturnStatus.Draft);
        SeedRma(2, 40m, status: ReturnStatus.Draft);
        SeedRma(3, 40m, status: ReturnStatus.Draft);

        Assert.False(await _sut.IsSerialReturnerAsync(7));
        var dashboard = await _sut.GetDashboardAsync(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 27));
        Assert.Empty(dashboard.FlaggedCustomers);
    }

    [Fact]
    public async Task Approving_the_third_rma_is_what_raises_the_flag_and_it_blocks_nothing()
    {
        _returns.PurchasedValue = 300m;
        SeedRma(1, 40m);
        SeedRma(2, 40m);
        var order = GivenOrder(id: 3, lines: new[] { ("TENT", 1, 40m) });
        var draft = await _sut.CreateAsync(Rma(order, (0, 1, "A")));
        Assert.False(draft.SerialReturner); // the draft itself is not counted

        var approved = await _sut.ApproveAsync(draft.Id, Staff);
        Assert.Equal(ReturnStatus.Approved, approved.Status);
        Assert.True(approved.SerialReturner);
    }

    [Fact]
    public async Task The_dashboard_lists_flagged_customers()
    {
        _returns.PurchasedValue = 300m;
        SeedRma(1, 40m);
        SeedRma(2, 40m);
        SeedRma(3, 40m);

        var dashboard = await _sut.GetDashboardAsync(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 27));

        var flagged = Assert.Single(dashboard.FlaggedCustomers);
        Assert.Equal(7, flagged.CustomerId);
        Assert.Equal("Ada Walker", flagged.CustomerName);
        Assert.Equal(3, flagged.DistinctOrders);
        Assert.Equal(120m, flagged.ReturnedValue);
        Assert.Equal(40m, flagged.RatePct);
    }

    [Fact]
    public async Task The_dashboard_rejects_an_inverted_date_range()
    {
        await Assert.ThrowsAsync<BusinessRuleException>(() => _sut.GetDashboardAsync(new DateOnly(2026, 10, 27), new DateOnly(2026, 10, 1)));
    }

    // ---- Desk privacy --------------------------------------------------------------------------

    [Theory]
    [InlineData(typeof(ReturnDto))]
    [InlineData(typeof(ReturnLineDto))]
    [InlineData(typeof(EligibilityDto))]
    [InlineData(typeof(EligibilityLineDto))]
    [InlineData(typeof(ReturnsDashboardDto))]
    [InlineData(typeof(ReturnRateRowDto))]
    [InlineData(typeof(FlaggedCustomerDto))]
    public void Desk_dtos_expose_no_address_date_of_birth_phone_or_email(Type dto)
    {
        var banned = new[] { "address", "postcode", "city", "dateofbirth", "dob", "birth", "phone", "mobile", "email" };
        foreach (var p in dto.GetProperties())
            Assert.DoesNotContain(banned, b => p.Name.Contains(b, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Desk_data_carries_only_the_customer_name()
    {
        var order = GivenOrder();
        var created = await _sut.CreateAsync(Rma(order, (0, 1, "A")));
        var eligibility = await _sut.GetEligibilityAsync(order.OrderNumber);

        Assert.Equal("Ada Walker", created.CustomerName);
        Assert.Equal("Ada Walker", eligibility.CustomerName);
    }
}
