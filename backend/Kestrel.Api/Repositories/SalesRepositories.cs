using Kestrel.Api.Data;
using Kestrel.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Kestrel.Api.Repositories;

public interface ICustomerRepository
{
    Task<(List<Customer> Items, int Total)> SearchAsync(string? query, int page, int pageSize);
    Task<Customer?> GetAsync(int id);
    Task<Customer?> GetReadOnlyAsync(int id);
    Task<Customer?> GetByEmailAsync(string email);
    Task AddAsync(Customer customer);
    Task<List<Customer>> ListAllAsync();
    Task SaveAsync();
}

public class CustomerRepository : ICustomerRepository
{
    private readonly KestrelDbContext _db;
    public CustomerRepository(KestrelDbContext db) => _db = db;

    public async Task<(List<Customer> Items, int Total)> SearchAsync(string? query, int page, int pageSize)
    {
        var q = _db.Customers.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(query))
            q = q.Where(c => c.LastName.Contains(query) || c.FirstName.Contains(query) || c.Email.Contains(query));
        var total = await q.CountAsync();
        var items = await q.OrderBy(c => c.LastName).ThenBy(c => c.FirstName).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        return (items, total);
    }

    public Task<Customer?> GetAsync(int id) => _db.Customers.FirstOrDefaultAsync(c => c.Id == id);
    public Task<Customer?> GetReadOnlyAsync(int id) => _db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id);
    public Task<Customer?> GetByEmailAsync(string email) => _db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Email == email);
    public async Task AddAsync(Customer customer) => await _db.Customers.AddAsync(customer);
    public Task<List<Customer>> ListAllAsync() => _db.Customers.AsNoTracking().ToListAsync();
    public Task SaveAsync() => _db.SaveChangesAsync();
}

public interface IOrderRepository
{
    Task<(List<Order> Items, int Total)> SearchAsync(int? storeId, int? customerId, string? status, int page, int pageSize);
    Task<Order?> GetWithDetailsAsync(int id);
    Task<Order?> GetForUpdateAsync(int id);
    Task<int> CountLinesAsync(int orderId);
    Task<int> CountItemsAsync(int orderId);
    Task<bool> OrderNumberExistsAsync(string orderNumber);
    Task<List<OrderNote>> ListNotesAsync(int orderId);
    Task<List<OrderStatusHistory>> ListHistoryAsync(int orderId);
    Task AddAsync(Order order);
    Task AddNoteAsync(OrderNote note);
    Task SaveAsync();
}

public class OrderRepository : IOrderRepository
{
    private readonly KestrelDbContext _db;
    public OrderRepository(KestrelDbContext db) => _db = db;

    public async Task<(List<Order> Items, int Total)> SearchAsync(int? storeId, int? customerId, string? status, int page, int pageSize)
    {
        var q = _db.Orders.AsNoTracking().AsQueryable();
        if (storeId.HasValue) q = q.Where(o => o.StoreId == storeId.Value);
        if (customerId.HasValue) q = q.Where(o => o.CustomerId == customerId.Value);
        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(o => o.Status == status);
        var total = await q.CountAsync();
        var items = await q.OrderByDescending(o => o.OrderedUtc).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        return (items, total);
    }

    public Task<Order?> GetWithDetailsAsync(int id) =>
        _db.Orders.AsNoTracking()
            .Include(o => o.Lines).ThenInclude(l => l.Product)
            .Include(o => o.Payments)
            .Include(o => o.History)
            .AsSplitQuery()
            .FirstOrDefaultAsync(o => o.Id == id);

    public Task<Order?> GetForUpdateAsync(int id) =>
        _db.Orders.Include(o => o.Lines).Include(o => o.Payments).Include(o => o.History).AsSplitQuery()
            .FirstOrDefaultAsync(o => o.Id == id);

    public Task<int> CountLinesAsync(int orderId) => _db.OrderLines.CountAsync(l => l.OrderId == orderId);
    public async Task<int> CountItemsAsync(int orderId) =>
        await _db.OrderLines.Where(l => l.OrderId == orderId).SumAsync(l => (int?)l.Quantity) ?? 0;
    public Task<bool> OrderNumberExistsAsync(string orderNumber) => _db.Orders.AnyAsync(o => o.OrderNumber == orderNumber);
    public Task<List<OrderNote>> ListNotesAsync(int orderId) =>
        _db.OrderNotes.AsNoTracking().Where(n => n.OrderId == orderId).OrderBy(n => n.CreatedUtc).ToListAsync();
    public Task<List<OrderStatusHistory>> ListHistoryAsync(int orderId) =>
        _db.OrderStatusHistory.AsNoTracking().Where(h => h.OrderId == orderId).OrderBy(h => h.ChangedUtc).ToListAsync();
    public async Task AddAsync(Order order) => await _db.Orders.AddAsync(order);
    public async Task AddNoteAsync(OrderNote note) => await _db.OrderNotes.AddAsync(note);
    public Task SaveAsync() => _db.SaveChangesAsync();
}

public interface IPaymentRepository
{
    Task<List<Payment>> ListForOrderAsync(int orderId);
    Task<Payment?> GetAsync(int id);
    Task AddAsync(Payment payment);
    Task SaveAsync();
}

public class PaymentRepository : IPaymentRepository
{
    private readonly KestrelDbContext _db;
    public PaymentRepository(KestrelDbContext db) => _db = db;

    public Task<List<Payment>> ListForOrderAsync(int orderId) =>
        _db.Payments.AsNoTracking().Where(p => p.OrderId == orderId).OrderBy(p => p.PaidUtc).ToListAsync();
    public Task<Payment?> GetAsync(int id) => _db.Payments.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id);
    public async Task AddAsync(Payment payment) => await _db.Payments.AddAsync(payment);
    public Task SaveAsync() => _db.SaveChangesAsync();
}

public interface IUserRepository
{
    Task<List<AppUser>> ListAsync();
    Task<AppUser?> GetAsync(int id);
}

public class UserRepository : IUserRepository
{
    private readonly KestrelDbContext _db;
    public UserRepository(KestrelDbContext db) => _db = db;

    public Task<List<AppUser>> ListAsync() => _db.Users.AsNoTracking().OrderBy(u => u.DisplayName).ToListAsync();
    public Task<AppUser?> GetAsync(int id) => _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id);
}

public interface IImportRepository
{
    Task<ImportBatch> AddBatchAsync(ImportBatch batch);
    Task AddErrorAsync(ImportError error);
    Task<List<ImportBatch>> ListAsync();
    Task<ImportBatch?> GetAsync(int id);
    Task SaveAsync();
}

public class ImportRepository : IImportRepository
{
    private readonly KestrelDbContext _db;
    public ImportRepository(KestrelDbContext db) => _db = db;

    public async Task<ImportBatch> AddBatchAsync(ImportBatch batch)
    {
        await _db.ImportBatches.AddAsync(batch);
        await _db.SaveChangesAsync();
        return batch;
    }

    public async Task AddErrorAsync(ImportError error) => await _db.ImportErrors.AddAsync(error);
    public Task<List<ImportBatch>> ListAsync() => _db.ImportBatches.AsNoTracking().OrderByDescending(b => b.Id).Take(50).ToListAsync();
    public Task<ImportBatch?> GetAsync(int id) => _db.ImportBatches.Include(b => b.Errors).FirstOrDefaultAsync(b => b.Id == id);
    public Task SaveAsync() => _db.SaveChangesAsync();
}

public record SalesOrderRow(int Id, int StoreId, string Channel, DateTime OrderedUtc, decimal Subtotal, decimal DiscountAmount, decimal TotalAmount);
public record TopProductRow(int ProductId, string Sku, string Name, int Units, decimal Revenue);

public interface IReportRepository
{
    Task<List<SalesOrderRow>> GetSalesOrdersAsync(DateTime fromUtcInclusive, DateTime toUtcExclusive);
    Task<List<TopProductRow>> GetTopProductsAsync(DateTime fromUtcInclusive, DateTime toUtcExclusive, int take);
}

public class ReportRepository : IReportRepository
{
    private readonly KestrelDbContext _db;
    public ReportRepository(KestrelDbContext db) => _db = db;

    public Task<List<SalesOrderRow>> GetSalesOrdersAsync(DateTime fromUtcInclusive, DateTime toUtcExclusive) =>
        _db.Orders.AsNoTracking()
            .Where(o => o.Status != OrderStatus.Cancelled && o.OrderedUtc >= fromUtcInclusive && o.OrderedUtc < toUtcExclusive)
            .Select(o => new SalesOrderRow(o.Id, o.StoreId, o.Channel, o.OrderedUtc, o.Subtotal, o.DiscountAmount, o.TotalAmount))
            .ToListAsync();

    public async Task<List<TopProductRow>> GetTopProductsAsync(DateTime fromUtcInclusive, DateTime toUtcExclusive, int take)
    {
        var rows = await _db.OrderLines.AsNoTracking()
            .Where(l => _db.Orders.Any(o => o.Id == l.OrderId && o.Status != OrderStatus.Cancelled
                                            && o.OrderedUtc >= fromUtcInclusive && o.OrderedUtc < toUtcExclusive))
            .GroupBy(l => new { l.ProductId, l.Product!.Sku, l.Product.Name })
            .Select(g => new { g.Key.ProductId, g.Key.Sku, g.Key.Name, Units = g.Sum(x => x.Quantity), Revenue = g.Sum(x => x.LineTotal) })
            .OrderByDescending(x => x.Units).Take(take)
            .ToListAsync();
        return rows.Select(r => new TopProductRow(r.ProductId, r.Sku, r.Name, r.Units, r.Revenue)).ToList();
    }
}
