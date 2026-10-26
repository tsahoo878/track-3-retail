using Kestrel.Api.Domain;
using Kestrel.Api.Dtos;

namespace Kestrel.Api.Mappers;

public static class CategoryMapper
{
    public static CategoryDto ToDto(Category c) => new(c.Id, c.Code, c.Name, c.Department, c.SortOrder);
    public static void Apply(Category c, SaveCategoryDto d)
    {
        c.Code = d.Code.Trim().ToUpperInvariant();
        c.Name = d.Name.Trim();
        c.Department = d.Department.Trim();
        c.SortOrder = d.SortOrder;
    }
}

public static class ProductMapper
{
    public static ProductDto ToDto(Product p) =>
        new(p.Id, p.Sku, p.Name, p.CategoryId, p.Category?.Name ?? "", p.Brand, p.UnitPrice, p.CostPrice, p.IsActive);

    public static void Apply(Product p, SaveProductDto d)
    {
        p.Sku = d.Sku.Trim().ToUpperInvariant();
        p.Name = d.Name.Trim();
        p.CategoryId = d.CategoryId;
        p.Brand = d.Brand.Trim();
        p.UnitPrice = d.UnitPrice;
        p.CostPrice = d.CostPrice;
    }
}

public static class StoreMapper
{
    public static StoreDto ToDto(Store s) => new(s.Id, s.Code, s.Name, s.Region, s.City, s.Timezone, s.IsActive);
    public static void Apply(Store s, SaveStoreDto d)
    {
        s.Code = d.Code.Trim().ToUpperInvariant();
        s.Name = d.Name.Trim();
        s.Region = d.Region.Trim();
        s.City = d.City.Trim();
        s.Timezone = d.Timezone.Trim();
        s.IsActive = d.IsActive;
    }
}

public static class StockMapper
{
    public static StockLevelDto ToDto(StockLevel s) =>
        new(s.Id, s.StoreId, s.Store?.Name ?? "", s.ProductId, s.Product?.Sku ?? "", s.Product?.Name ?? "", s.Quantity, s.ReorderLevel, s.UpdatedUtc);
}

public static class CustomerMapper
{
    public static CustomerDto ToDto(Customer c) =>
        new(c.Id, c.Email, c.FirstName, c.LastName, c.Phone, c.AddressLine1, c.City, c.Postcode, c.DateOfBirth,
            c.LoyaltyTier, c.Region, c.MarketingOptIn, c.CreatedUtc);

    public static void Apply(Customer c, SaveCustomerDto d)
    {
        c.Email = d.Email.Trim().ToLowerInvariant();
        c.FirstName = d.FirstName.Trim();
        c.LastName = d.LastName.Trim();
        c.Phone = d.Phone;
        c.AddressLine1 = d.AddressLine1;
        c.City = d.City;
        c.Postcode = d.Postcode;
        c.DateOfBirth = d.DateOfBirth;
        c.LoyaltyTier = d.LoyaltyTier;
        c.Region = d.Region;
        c.MarketingOptIn = d.MarketingOptIn;
    }
}

public static class OrderMapper
{
    public static PaymentDto ToDto(Payment p) =>
        new(p.Id, p.OrderId, p.Method, p.Amount, p.CardLast4, p.ProviderRef, p.Status, p.PaidUtc);

    public static OrderLineDto ToDto(OrderLine l) =>
        new(l.Id, l.ProductId, l.Product?.Sku ?? "", l.Product?.Name ?? "", l.Quantity, l.UnitPrice, l.LineTotal);

    public static StatusHistoryDto ToDto(OrderStatusHistory h) =>
        new(h.Id, h.FromStatus, h.ToStatus, h.ChangedUtc, h.ChangedByUserId);

    public static OrderNoteDto ToDto(OrderNote n) => new(n.Id, n.AuthorUserId, n.Note, n.CreatedUtc);

    public static OrderDetailDto ToDetail(Order o, Customer? customer, Store? store) =>
        new(o.Id, o.OrderNumber, o.CustomerId, customer is null ? "" : $"{customer.FirstName} {customer.LastName}",
            o.StoreId, store?.Name ?? "", o.Channel, o.Status, o.OrderedUtc, o.Subtotal, o.DiscountAmount, o.TotalAmount,
            o.Lines.Select(ToDto).ToList(), o.Payments.Select(ToDto).ToList(),
            o.History.OrderBy(h => h.ChangedUtc).Select(ToDto).ToList());
}

public static class UserMapper
{
    public static UserDto ToDto(AppUser u) =>
        new(u.Id, u.Email, u.DisplayName, u.PasswordHash, u.Role, u.StoreId, u.IsActive, u.CreatedUtc);
}

public static class ImportMapper
{
    public static ImportBatchDto ToDto(ImportBatch b) =>
        new(b.Id, b.FileName, b.StartedUtc, b.FinishedUtc, b.TotalLines, b.AppliedLines, b.RejectedLines, b.Status);

    public static ImportBatchDetailDto ToDetail(ImportBatch b) =>
        new(b.Id, b.FileName, b.StartedUtc, b.FinishedUtc, b.TotalLines, b.AppliedLines, b.RejectedLines, b.Status,
            b.Errors.OrderBy(e => e.LineNumber).Select(e => new ImportErrorDto(e.LineNumber, e.Message)).ToList());
}
