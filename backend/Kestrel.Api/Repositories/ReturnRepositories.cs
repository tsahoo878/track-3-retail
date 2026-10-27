using Kestrel.Api.Data;
using Kestrel.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Kestrel.Api.Repositories;

// Flat rows for the dashboard; grouping happens in the service.
public record SoldLineRow(int ProductId, string Sku, string ProductName, int CategoryId, string CategoryName, int StoreId, string StoreName, int Quantity);
public record ReturnedLineRow(int ProductId, string Sku, string ProductName, int CategoryId, string CategoryName, int StoreId, string StoreName, int Quantity, decimal RefundAmount);

public interface IReturnRepository
{
    Task<Return?> GetWithLinesAsync(int id);
    Task<Return?> GetWithLinesForUpdateAsync(int id);
    Task<List<Return>> ListForOrderAsync(int orderId);
    Task<List<Return>> ListForCustomerSinceAsync(int customerId, DateTime sinceUtc);
    Task<decimal> SumPurchasedSinceAsync(int customerId, DateTime sinceUtc);
    Task<List<int>> ListCustomerIdsWithReturnsSinceAsync(DateTime sinceUtc);
    Task<bool> RmaNumberExistsAsync(string rmaNumber);
    Task<List<SoldLineRow>> GetSoldLinesAsync(DateTime fromUtcInclusive, DateTime toUtcExclusive);
    Task<List<ReturnedLineRow>> GetReturnedLinesAsync(DateTime fromUtcInclusive, DateTime toUtcExclusive);
    Task AddAsync(Return rma);
    Task SaveAsync();

    // Concurrency hooks. Defaults are no-ops so in-memory fakes need no changes; ReturnRepository overrides them.
    // Runs work inside one database transaction (commit on success, rollback if it throws).
    Task<T> InTransactionAsync<T>(Func<Task<T>> work) => work();
    // Takes a row lock on the order (SELECT ... FOR UPDATE) until the surrounding transaction ends.
    Task LockOrderAsync(int orderId) => Task.CompletedTask;
}

public class ReturnRepository : IReturnRepository
{
    private readonly KestrelDbContext _db;
    public ReturnRepository(KestrelDbContext db) => _db = db;

    public Task<Return?> GetWithLinesAsync(int id) =>
        _db.Returns.AsNoTracking().Include(r => r.Lines).FirstOrDefaultAsync(r => r.Id == id);

    // Must be called inside InTransactionAsync: SELECT ... FOR UPDATE holds the row lock until commit/rollback,
    // and reads the committed latest row, so callers can safely re-check status after it returns.
    public Task<Return?> GetWithLinesForUpdateAsync(int id) =>
        _db.Returns.FromSqlInterpolated($"SELECT * FROM returns WHERE id = {id} FOR UPDATE")
            .Include(r => r.Lines).FirstOrDefaultAsync();

    public async Task<T> InTransactionAsync<T>(Func<Task<T>> work)
    {
        if (_db.Database.CurrentTransaction != null) return await work();
        await using var tx = await _db.Database.BeginTransactionAsync();
        var result = await work();
        await tx.CommitAsync();
        return result;
    }

    public async Task LockOrderAsync(int orderId) =>
        await _db.Database.ExecuteSqlInterpolatedAsync($"SELECT id FROM orders WHERE id = {orderId} FOR UPDATE");

    // All statuses are included; callers decide which statuses count.
    public Task<List<Return>> ListForOrderAsync(int orderId) =>
        _db.Returns.AsNoTracking().Include(r => r.Lines).Where(r => r.OrderId == orderId).OrderBy(r => r.Id).ToListAsync();

    public Task<List<Return>> ListForCustomerSinceAsync(int customerId, DateTime sinceUtc) =>
        _db.Returns.AsNoTracking().Where(r => r.CustomerId == customerId && r.CreatedUtc >= sinceUtc).ToListAsync();

    public async Task<decimal> SumPurchasedSinceAsync(int customerId, DateTime sinceUtc) =>
        await _db.Orders.AsNoTracking()
            .Where(o => o.CustomerId == customerId && o.Status != OrderStatus.Cancelled && o.OrderedUtc >= sinceUtc)
            .SumAsync(o => (decimal?)o.TotalAmount) ?? 0m;

    public Task<List<int>> ListCustomerIdsWithReturnsSinceAsync(DateTime sinceUtc) =>
        _db.Returns.AsNoTracking()
            .Where(r => r.Status == ReturnStatus.Approved && r.CreatedUtc >= sinceUtc)
            .Select(r => r.CustomerId).Distinct().ToListAsync();

    public Task<bool> RmaNumberExistsAsync(string rmaNumber) => _db.Returns.AnyAsync(r => r.RmaNumber == rmaNumber);

    public Task<List<SoldLineRow>> GetSoldLinesAsync(DateTime fromUtcInclusive, DateTime toUtcExclusive) =>
        (from l in _db.OrderLines.AsNoTracking()
         join o in _db.Orders on l.OrderId equals o.Id
         join p in _db.Products on l.ProductId equals p.Id
         join c in _db.Categories on p.CategoryId equals c.Id
         join s in _db.Stores on o.StoreId equals s.Id
         where o.Status != OrderStatus.Cancelled && o.OrderedUtc >= fromUtcInclusive && o.OrderedUtc < toUtcExclusive
         select new SoldLineRow(p.Id, p.Sku, p.Name, c.Id, c.Name, s.Id, s.Name, l.Quantity)).ToListAsync();

    // Returns are attributed to the store that received them.
    public Task<List<ReturnedLineRow>> GetReturnedLinesAsync(DateTime fromUtcInclusive, DateTime toUtcExclusive) =>
        (from rl in _db.ReturnLines.AsNoTracking()
         join r in _db.Returns on rl.ReturnId equals r.Id
         join l in _db.OrderLines on rl.OrderLineId equals l.Id
         join p in _db.Products on l.ProductId equals p.Id
         join c in _db.Categories on p.CategoryId equals c.Id
         join s in _db.Stores on r.StoreId equals s.Id
         where r.Status == ReturnStatus.Approved && r.CreatedUtc >= fromUtcInclusive && r.CreatedUtc < toUtcExclusive
         select new ReturnedLineRow(p.Id, p.Sku, p.Name, c.Id, c.Name, s.Id, s.Name, rl.Quantity, rl.RefundAmount)).ToListAsync();

    public async Task AddAsync(Return rma) => await _db.Returns.AddAsync(rma);
    public Task SaveAsync() => _db.SaveChangesAsync();
}
