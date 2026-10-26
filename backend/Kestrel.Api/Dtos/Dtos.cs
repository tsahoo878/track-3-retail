namespace Kestrel.Api.Dtos;

public record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);

// Categories
public record CategoryDto(int Id, string Code, string Name, string Department, int SortOrder);
public record SaveCategoryDto(string Code, string Name, string Department, int SortOrder);

// Products
public record ProductDto(int Id, string Sku, string Name, int CategoryId, string CategoryName, string Brand, decimal UnitPrice, decimal CostPrice, bool IsActive);
public record SaveProductDto(string Sku, string Name, int CategoryId, string Brand, decimal UnitPrice, decimal CostPrice);

// Stores
public record StoreDto(int Id, string Code, string Name, string Region, string City, string Timezone, bool IsActive);
public record SaveStoreDto(string Code, string Name, string Region, string City, string Timezone, bool IsActive);

// Stock
public record StockLevelDto(int Id, int StoreId, string StoreName, int ProductId, string Sku, string ProductName, int Quantity, int ReorderLevel, DateTime UpdatedUtc);
public record StockAdjustDto(int StoreId, int ProductId, int Delta, string Reason);
public record SetStockDto(int Quantity, int ReorderLevel);

// Customers
public record CustomerDto(int Id, string Email, string FirstName, string LastName, string? Phone, string? AddressLine1,
    string? City, string? Postcode, DateTime? DateOfBirth, string LoyaltyTier, string Region, bool MarketingOptIn, DateTime CreatedUtc);
public record SaveCustomerDto(string Email, string FirstName, string LastName, string? Phone, string? AddressLine1,
    string? City, string? Postcode, DateTime? DateOfBirth, string LoyaltyTier, string Region, bool MarketingOptIn);

// Orders
public record OrderListItemDto(int Id, string OrderNumber, string CustomerName, string StoreName, string Channel, string Status,
    DateTime OrderedUtc, int LineCount, int ItemCount, decimal TotalAmount);
public record OrderLineDto(int Id, int ProductId, string Sku, string ProductName, int Quantity, decimal UnitPrice, decimal LineTotal);
public record PaymentDto(int Id, int OrderId, string Method, decimal Amount, string? CardLast4, string? ProviderRef, string Status, DateTime PaidUtc);
public record StatusHistoryDto(int Id, string? FromStatus, string ToStatus, DateTime ChangedUtc, int? ChangedByUserId);
public record OrderNoteDto(int Id, int AuthorUserId, string Note, DateTime CreatedUtc);
public record OrderDetailDto(int Id, string OrderNumber, int CustomerId, string CustomerName, int StoreId, string StoreName, string Channel,
    string Status, DateTime OrderedUtc, decimal Subtotal, decimal DiscountAmount, decimal TotalAmount,
    IReadOnlyList<OrderLineDto> Lines, IReadOnlyList<PaymentDto> Payments, IReadOnlyList<StatusHistoryDto> History);
public record CreateOrderLineDto(int ProductId, int Quantity);
public record CreateOrderDto(int CustomerId, int StoreId, string Channel, List<CreateOrderLineDto> Lines);
public record ChangeStatusDto(string Status, int? UserId);
public record AddNoteDto(int AuthorUserId, string Note);

// Payments
public record CreatePaymentDto(string Method, decimal Amount, string? CardLast4, string? ProviderRef);

// Users
public record UserDto(int Id, string Email, string DisplayName, string PasswordHash, string Role, int? StoreId, bool IsActive, DateTime CreatedUtc);

// Reports
public record ChannelSalesDto(string Channel, int OrderCount, decimal NetSales);
public record StoreSalesDto(int StoreId, string StoreName, int OrderCount, decimal NetSales);
public record DailySalesDto(DateOnly Date, int OrderCount, decimal GrossSales, decimal Discounts, decimal NetSales,
    IReadOnlyList<ChannelSalesDto> ByChannel, IReadOnlyList<StoreSalesDto> ByStore);
public record DailySalesSummaryDto(DateOnly Date, int OrderCount, decimal NetSales);
public record TopProductDto(int ProductId, string Sku, string Name, int UnitsSold, decimal Revenue);

// Imports
public record ImportErrorDto(int LineNumber, string Message);
public record ImportBatchDto(int Id, string FileName, DateTime StartedUtc, DateTime? FinishedUtc, int TotalLines, int AppliedLines, int RejectedLines, string Status);
public record ImportBatchDetailDto(int Id, string FileName, DateTime StartedUtc, DateTime? FinishedUtc, int TotalLines, int AppliedLines, int RejectedLines, string Status, IReadOnlyList<ImportErrorDto> Errors);
