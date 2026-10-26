namespace Kestrel.Api.Domain;

public class Category
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Department { get; set; } = "";
    public int SortOrder { get; set; }
}

public class Store
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Region { get; set; } = "";
    public string City { get; set; } = "";
    public string Timezone { get; set; } = "Europe/London";
    public bool IsActive { get; set; } = true;
}

public class AppUser
{
    public int Id { get; set; }
    public string Email { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string Role { get; set; } = "";
    public int? StoreId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedUtc { get; set; }
}

public class Product
{
    public int Id { get; set; }
    public string Sku { get; set; } = "";
    public string Name { get; set; } = "";
    public int CategoryId { get; set; }
    public string Brand { get; set; } = "";
    public decimal UnitPrice { get; set; }
    public decimal CostPrice { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedUtc { get; set; }
    public Category? Category { get; set; }
}

public class StockLevel
{
    public int Id { get; set; }
    public int StoreId { get; set; }
    public int ProductId { get; set; }
    public int Quantity { get; set; }
    public int ReorderLevel { get; set; }
    public DateTime UpdatedUtc { get; set; }
    public Store? Store { get; set; }
    public Product? Product { get; set; }
}

public class Customer
{
    public int Id { get; set; }
    public string Email { get; set; } = "";
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string? Phone { get; set; }
    public string? AddressLine1 { get; set; }
    public string? City { get; set; }
    public string? Postcode { get; set; }
    public DateTime? DateOfBirth { get; set; }
    public string LoyaltyTier { get; set; } = "NONE";
    public string Region { get; set; } = "";
    public bool MarketingOptIn { get; set; }
    public DateTime CreatedUtc { get; set; }
}

public static class OrderStatus
{
    public const string Placed = "PLACED";
    public const string Paid = "PAID";
    public const string Fulfilled = "FULFILLED";
    public const string Cancelled = "CANCELLED";
}

public class Order
{
    public int Id { get; set; }
    public string OrderNumber { get; set; } = "";
    public int CustomerId { get; set; }
    public int StoreId { get; set; }
    public string Channel { get; set; } = "STORE";
    public string Status { get; set; } = OrderStatus.Placed;
    public DateTime OrderedUtc { get; set; }
    public decimal Subtotal { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public List<OrderLine> Lines { get; set; } = new();
    public List<Payment> Payments { get; set; } = new();
    public List<OrderStatusHistory> History { get; set; } = new();
    public List<OrderNote> Notes { get; set; } = new();
}

public class OrderLine
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public int ProductId { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal LineTotal { get; set; }
    public Product? Product { get; set; }
}

public class Payment
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public string Method { get; set; } = "CARD";
    public decimal Amount { get; set; }
    public string? CardLast4 { get; set; }
    public string? ProviderRef { get; set; }
    public string Status { get; set; } = "CAPTURED";
    public DateTime PaidUtc { get; set; }
}

public class OrderStatusHistory
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public string? FromStatus { get; set; }
    public string ToStatus { get; set; } = "";
    public DateTime ChangedUtc { get; set; }
    public int? ChangedByUserId { get; set; }
}

public class OrderNote
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public int AuthorUserId { get; set; }
    public string Note { get; set; } = "";
    public DateTime CreatedUtc { get; set; }
}

public class ImportBatch
{
    public int Id { get; set; }
    public string FileName { get; set; } = "";
    public DateTime StartedUtc { get; set; }
    public DateTime? FinishedUtc { get; set; }
    public int TotalLines { get; set; }
    public int AppliedLines { get; set; }
    public int RejectedLines { get; set; }
    public string Status { get; set; } = "RUNNING";
    public List<ImportError> Errors { get; set; } = new();
}

public class ImportError
{
    public int Id { get; set; }
    public int BatchId { get; set; }
    public int LineNumber { get; set; }
    public string Message { get; set; } = "";
}
