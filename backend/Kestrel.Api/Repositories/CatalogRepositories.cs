using Kestrel.Api.Data;
using Kestrel.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Kestrel.Api.Repositories;

public interface ICategoryRepository
{
    Task<List<Category>> ListAsync();
    Task<Category?> GetAsync(int id);
    Task<Category?> GetByCodeAsync(string code);
    Task AddAsync(Category category);
    Task SaveAsync();
}

public class CategoryRepository : ICategoryRepository
{
    private readonly KestrelDbContext _db;
    public CategoryRepository(KestrelDbContext db) => _db = db;

    public Task<List<Category>> ListAsync() => _db.Categories.AsNoTracking().OrderBy(c => c.SortOrder).ToListAsync();
    public Task<Category?> GetAsync(int id) => _db.Categories.FirstOrDefaultAsync(c => c.Id == id);
    public Task<Category?> GetByCodeAsync(string code) => _db.Categories.AsNoTracking().FirstOrDefaultAsync(c => c.Code == code);
    public async Task AddAsync(Category category) => await _db.Categories.AddAsync(category);
    public Task SaveAsync() => _db.SaveChangesAsync();
}

public interface IProductRepository
{
    Task<(List<Product> Items, int Total)> SearchAsync(string? query, int? categoryId, bool includeInactive, int page, int pageSize);
    Task<Product?> GetAsync(int id);
    Task<Product?> GetBySkuAsync(string sku);
    Task AddAsync(Product product);
    Task SaveAsync();
}

public class ProductRepository : IProductRepository
{
    private readonly KestrelDbContext _db;
    public ProductRepository(KestrelDbContext db) => _db = db;

    public async Task<(List<Product> Items, int Total)> SearchAsync(string? query, int? categoryId, bool includeInactive, int page, int pageSize)
    {
        var q = _db.Products.Include(p => p.Category).AsQueryable();
        if (!includeInactive) q = q.Where(p => p.IsActive);
        if (categoryId.HasValue) q = q.Where(p => p.CategoryId == categoryId.Value);
        if (!string.IsNullOrWhiteSpace(query))
            q = q.Where(p => p.Name.Contains(query) || p.Sku.Contains(query));
        var total = await q.CountAsync();
        var items = await q.OrderBy(p => p.Name).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        return (items, total);
    }

    public Task<Product?> GetAsync(int id) => _db.Products.Include(p => p.Category).FirstOrDefaultAsync(p => p.Id == id);
    public Task<Product?> GetBySkuAsync(string sku) => _db.Products.FirstOrDefaultAsync(p => p.Sku == sku);
    public async Task AddAsync(Product product) => await _db.Products.AddAsync(product);
    public Task SaveAsync() => _db.SaveChangesAsync();
}

public interface IStoreRepository
{
    Task<List<Store>> ListAsync();
    Task<Store?> GetAsync(int id);
    Task<Store?> GetByCodeAsync(string code);
    Task AddAsync(Store store);
    Task SaveAsync();
}

public class StoreRepository : IStoreRepository
{
    private readonly KestrelDbContext _db;
    public StoreRepository(KestrelDbContext db) => _db = db;

    public Task<List<Store>> ListAsync() => _db.Stores.AsNoTracking().OrderBy(s => s.Name).ToListAsync();
    public Task<Store?> GetAsync(int id) => _db.Stores.FirstOrDefaultAsync(s => s.Id == id);
    public Task<Store?> GetByCodeAsync(string code) => _db.Stores.AsNoTracking().FirstOrDefaultAsync(s => s.Code == code);
    public async Task AddAsync(Store store) => await _db.Stores.AddAsync(store);
    public Task SaveAsync() => _db.SaveChangesAsync();
}

public interface IStockRepository
{
    Task<List<StockLevel>> ListAsync(int? storeId, int? productId, bool lowOnly);
    Task<StockLevel?> GetAsync(int id);
    Task<StockLevel?> GetForStoreProductAsync(int storeId, int productId);
    Task AddAsync(StockLevel stock);
    Task SaveAsync();
}

public class StockRepository : IStockRepository
{
    private readonly KestrelDbContext _db;
    public StockRepository(KestrelDbContext db) => _db = db;

    public Task<List<StockLevel>> ListAsync(int? storeId, int? productId, bool lowOnly)
    {
        var q = _db.StockLevels.AsNoTracking().Include(s => s.Store).Include(s => s.Product).AsQueryable();
        if (storeId.HasValue) q = q.Where(s => s.StoreId == storeId.Value);
        if (productId.HasValue) q = q.Where(s => s.ProductId == productId.Value);
        if (lowOnly) q = q.Where(s => s.Quantity <= s.ReorderLevel);
        return q.OrderBy(s => s.Store!.Name).ThenBy(s => s.Product!.Name).Take(500).ToListAsync();
    }

    public Task<StockLevel?> GetAsync(int id) =>
        _db.StockLevels.Include(s => s.Store).Include(s => s.Product).FirstOrDefaultAsync(s => s.Id == id);

    public Task<StockLevel?> GetForStoreProductAsync(int storeId, int productId) =>
        _db.StockLevels.FirstOrDefaultAsync(s => s.StoreId == storeId && s.ProductId == productId);

    public async Task AddAsync(StockLevel stock) => await _db.StockLevels.AddAsync(stock);
    public Task SaveAsync() => _db.SaveChangesAsync();
}
