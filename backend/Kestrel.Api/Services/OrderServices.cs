using FluentValidation;
using Kestrel.Api.Domain;
using Kestrel.Api.Dtos;
using Kestrel.Api.Infrastructure;
using Kestrel.Api.Mappers;
using Kestrel.Api.Repositories;

namespace Kestrel.Api.Services;

public static class OrderPricing
{
    public static decimal DiscountPercent(string loyaltyTier) => loyaltyTier switch
    {
        "SILVER" => 5m,
        "GOLD" => 10m,
        _ => 0m
    };

    public static decimal Discount(decimal subtotal, string loyaltyTier) =>
        Math.Round(subtotal * DiscountPercent(loyaltyTier) / 100m, 2, MidpointRounding.AwayFromZero);

    public static decimal Total(decimal subtotal, string loyaltyTier) => subtotal - Discount(subtotal, loyaltyTier);
}

public interface IOrderService
{
    Task<PagedResult<OrderListItemDto>> SearchAsync(int? storeId, int? customerId, string? status, int page, int pageSize);
    Task<OrderDetailDto> GetAsync(int id);
    Task<OrderDetailDto> CreateAsync(CreateOrderDto dto);
    Task<OrderDetailDto> ChangeStatusAsync(int id, ChangeStatusDto dto);
    Task<List<StatusHistoryDto>> GetHistoryAsync(int id);
    Task<List<OrderNoteDto>> GetNotesAsync(int id);
    Task<OrderNoteDto> AddNoteAsync(int id, AddNoteDto dto);
    Task<List<OrderListItemDto>> ListForCustomerAsync(int customerId);
}

public class OrderService : IOrderService
{
    private static readonly Dictionary<string, string[]> Transitions = new()
    {
        [OrderStatus.Placed] = new[] { OrderStatus.Paid, OrderStatus.Cancelled },
        [OrderStatus.Paid] = new[] { OrderStatus.Fulfilled, OrderStatus.Cancelled },
        [OrderStatus.Fulfilled] = Array.Empty<string>(),
        [OrderStatus.Cancelled] = Array.Empty<string>()
    };

    private readonly IOrderRepository _orders;
    private readonly ICustomerRepository _customers;
    private readonly IStoreRepository _stores;
    private readonly IProductRepository _products;
    private readonly IStockRepository _stock;
    private readonly IValidator<CreateOrderDto> _createValidator;
    private readonly IValidator<ChangeStatusDto> _statusValidator;
    private readonly IValidator<AddNoteDto> _noteValidator;

    public OrderService(IOrderRepository orders, ICustomerRepository customers, IStoreRepository stores,
        IProductRepository products, IStockRepository stock, IValidator<CreateOrderDto> createValidator,
        IValidator<ChangeStatusDto> statusValidator, IValidator<AddNoteDto> noteValidator)
    {
        _orders = orders;
        _customers = customers;
        _stores = stores;
        _products = products;
        _stock = stock;
        _createValidator = createValidator;
        _statusValidator = statusValidator;
        _noteValidator = noteValidator;
    }

    public async Task<PagedResult<OrderListItemDto>> SearchAsync(int? storeId, int? customerId, string? status, int page, int pageSize)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var (items, total) = await _orders.SearchAsync(storeId, customerId, status, page, pageSize);
        var result = await BuildListAsync(items);
        return new PagedResult<OrderListItemDto>(result, total, page, pageSize);
    }

    public async Task<List<OrderListItemDto>> ListForCustomerAsync(int customerId)
    {
        if (await _customers.GetReadOnlyAsync(customerId) == null)
            throw new NotFoundException($"Customer {customerId} was not found.");
        var (items, _) = await _orders.SearchAsync(null, customerId, null, 1, 100);
        return await BuildListAsync(items);
    }

    private async Task<List<OrderListItemDto>> BuildListAsync(List<Order> items)
    {
        var result = new List<OrderListItemDto>();
        foreach (var o in items)
        {
            var customer = await _customers.GetReadOnlyAsync(o.CustomerId);
            var store = await _stores.GetAsync(o.StoreId);
            var lineCount = await _orders.CountLinesAsync(o.Id);
            var itemCount = await _orders.CountItemsAsync(o.Id);
            result.Add(new OrderListItemDto(o.Id, o.OrderNumber,
                customer == null ? "" : $"{customer.FirstName} {customer.LastName}",
                store?.Name ?? "", o.Channel, o.Status, o.OrderedUtc, lineCount, itemCount, o.TotalAmount));
        }
        return result;
    }

    public async Task<OrderDetailDto> GetAsync(int id)
    {
        var order = await _orders.GetWithDetailsAsync(id) ?? throw new NotFoundException($"Order {id} was not found.");
        var customer = await _customers.GetReadOnlyAsync(order.CustomerId);
        var store = await _stores.GetAsync(order.StoreId);
        return OrderMapper.ToDetail(order, customer, store);
    }

    public async Task<OrderDetailDto> CreateAsync(CreateOrderDto dto)
    {
        await _createValidator.ValidateAndThrowAsync(dto);
        var customer = await _customers.GetReadOnlyAsync(dto.CustomerId)
            ?? throw new NotFoundException($"Customer {dto.CustomerId} was not found.");
        var store = await _stores.GetAsync(dto.StoreId) ?? throw new NotFoundException($"Store {dto.StoreId} was not found.");
        if (!store.IsActive) throw new BusinessRuleException($"Store {store.Name} is closed.");

        var now = DateTime.UtcNow;
        var order = new Order
        {
            CustomerId = customer.Id,
            StoreId = store.Id,
            Channel = dto.Channel,
            Status = OrderStatus.Placed,
            OrderedUtc = now
        };

        decimal subtotal = 0;
        foreach (var line in dto.Lines)
        {
            var product = await _products.GetAsync(line.ProductId)
                ?? throw new NotFoundException($"Product {line.ProductId} was not found.");
            if (!product.IsActive) throw new BusinessRuleException($"{product.Name} is no longer sold.");

            var stock = await _stock.GetForStoreProductAsync(store.Id, product.Id);
            if (stock == null || stock.Quantity < line.Quantity)
                throw new BusinessRuleException($"Not enough stock of {product.Sku} at {store.Name}.");
            stock.Quantity -= line.Quantity;
            stock.UpdatedUtc = now;

            var lineTotal = product.UnitPrice * line.Quantity;
            subtotal += lineTotal;
            order.Lines.Add(new OrderLine
            {
                ProductId = product.Id,
                Quantity = line.Quantity,
                UnitPrice = product.UnitPrice,
                LineTotal = lineTotal
            });
        }

        order.Subtotal = subtotal;
        order.DiscountAmount = OrderPricing.Discount(subtotal, customer.LoyaltyTier);
        order.TotalAmount = subtotal - order.DiscountAmount;
        order.OrderNumber = await NextOrderNumberAsync(now);
        order.History.Add(new OrderStatusHistory { FromStatus = null, ToStatus = OrderStatus.Placed, ChangedUtc = now });

        await _orders.AddAsync(order);
        await _orders.SaveAsync();
        return await GetAsync(order.Id);
    }

    private async Task<string> NextOrderNumberAsync(DateTime now)
    {
        while (true)
        {
            var candidate = $"KO-{now:yyyyMMdd}-{Random.Shared.Next(1000, 9999)}";
            if (!await _orders.OrderNumberExistsAsync(candidate)) return candidate;
        }
    }

    public async Task<OrderDetailDto> ChangeStatusAsync(int id, ChangeStatusDto dto)
    {
        await _statusValidator.ValidateAndThrowAsync(dto);
        var order = await _orders.GetForUpdateAsync(id) ?? throw new NotFoundException($"Order {id} was not found.");
        var target = dto.Status.Trim().ToUpperInvariant();

        if (!Transitions.TryGetValue(order.Status, out var allowed) || !allowed.Contains(target))
            throw new BusinessRuleException($"An order that is {order.Status} cannot move to {target}.");

        if (target == OrderStatus.Paid)
        {
            var paid = order.Payments.Where(p => p.Status == "CAPTURED").Sum(p => p.Amount);
            if (paid < order.TotalAmount)
                throw new BusinessRuleException("The order has not been paid in full.");
        }

        if (target == OrderStatus.Cancelled)
        {
            foreach (var line in order.Lines)
            {
                var stock = await _stock.GetForStoreProductAsync(order.StoreId, line.ProductId);
                if (stock != null)
                {
                    stock.Quantity += line.Quantity;
                    stock.UpdatedUtc = DateTime.UtcNow;
                }
            }
        }

        var previous = order.Status;
        order.Status = target;
        order.History.Add(new OrderStatusHistory
        {
            FromStatus = previous,
            ToStatus = target,
            ChangedUtc = DateTime.UtcNow,
            ChangedByUserId = dto.UserId
        });
        await _orders.SaveAsync();
        return await GetAsync(id);
    }

    public async Task<List<StatusHistoryDto>> GetHistoryAsync(int id)
    {
        if (await _orders.GetWithDetailsAsync(id) == null) throw new NotFoundException($"Order {id} was not found.");
        return (await _orders.ListHistoryAsync(id)).Select(OrderMapper.ToDto).ToList();
    }

    public async Task<List<OrderNoteDto>> GetNotesAsync(int id)
    {
        if (await _orders.GetWithDetailsAsync(id) == null) throw new NotFoundException($"Order {id} was not found.");
        return (await _orders.ListNotesAsync(id)).Select(OrderMapper.ToDto).ToList();
    }

    public async Task<OrderNoteDto> AddNoteAsync(int id, AddNoteDto dto)
    {
        await _noteValidator.ValidateAndThrowAsync(dto);
        if (await _orders.GetWithDetailsAsync(id) == null) throw new NotFoundException($"Order {id} was not found.");
        var note = new OrderNote { OrderId = id, AuthorUserId = dto.AuthorUserId, Note = dto.Note.Trim(), CreatedUtc = DateTime.UtcNow };
        await _orders.AddNoteAsync(note);
        await _orders.SaveAsync();
        return OrderMapper.ToDto(note);
    }
}

public interface IPaymentService
{
    Task<List<PaymentDto>> ListForOrderAsync(int orderId);
    Task<PaymentDto> GetAsync(int id);
    Task<PaymentDto> AddAsync(int orderId, CreatePaymentDto dto);
}

public class PaymentService : IPaymentService
{
    private readonly IPaymentRepository _payments;
    private readonly IOrderRepository _orders;
    private readonly IValidator<CreatePaymentDto> _validator;

    public PaymentService(IPaymentRepository payments, IOrderRepository orders, IValidator<CreatePaymentDto> validator)
    {
        _payments = payments;
        _orders = orders;
        _validator = validator;
    }

    public async Task<List<PaymentDto>> ListForOrderAsync(int orderId)
    {
        if (await _orders.GetWithDetailsAsync(orderId) == null) throw new NotFoundException($"Order {orderId} was not found.");
        return (await _payments.ListForOrderAsync(orderId)).Select(OrderMapper.ToDto).ToList();
    }

    public async Task<PaymentDto> GetAsync(int id)
    {
        var p = await _payments.GetAsync(id) ?? throw new NotFoundException($"Payment {id} was not found.");
        return OrderMapper.ToDto(p);
    }

    public async Task<PaymentDto> AddAsync(int orderId, CreatePaymentDto dto)
    {
        await _validator.ValidateAndThrowAsync(dto);
        var order = await _orders.GetForUpdateAsync(orderId) ?? throw new NotFoundException($"Order {orderId} was not found.");
        if (order.Status == OrderStatus.Cancelled || order.Status == OrderStatus.Fulfilled)
            throw new BusinessRuleException($"A {order.Status} order cannot take a payment.");

        var alreadyPaid = order.Payments.Where(p => p.Status == "CAPTURED").Sum(p => p.Amount);
        if (alreadyPaid + dto.Amount > order.TotalAmount)
            throw new BusinessRuleException("The payment is more than the amount still owing.");

        var now = DateTime.UtcNow;
        var payment = new Payment
        {
            OrderId = orderId,
            Method = dto.Method,
            Amount = dto.Amount,
            CardLast4 = dto.CardLast4,
            ProviderRef = dto.ProviderRef,
            Status = "CAPTURED",
            PaidUtc = now
        };
        order.Payments.Add(payment);

        if (alreadyPaid + dto.Amount == order.TotalAmount && order.Status == OrderStatus.Placed)
        {
            order.Status = OrderStatus.Paid;
            order.History.Add(new OrderStatusHistory { FromStatus = OrderStatus.Placed, ToStatus = OrderStatus.Paid, ChangedUtc = now });
        }

        await _orders.SaveAsync();
        return OrderMapper.ToDto(payment);
    }
}
