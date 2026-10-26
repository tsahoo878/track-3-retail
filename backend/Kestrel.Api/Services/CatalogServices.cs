using FluentValidation;
using Kestrel.Api.Domain;
using Kestrel.Api.Dtos;
using Kestrel.Api.Infrastructure;
using Kestrel.Api.Mappers;
using Kestrel.Api.Repositories;

namespace Kestrel.Api.Services;

public interface ICategoryService
{
    Task<List<CategoryDto>> ListAsync();
    Task<CategoryDto> GetAsync(int id);
    Task<CategoryDto> CreateAsync(SaveCategoryDto dto);
    Task<CategoryDto> UpdateAsync(int id, SaveCategoryDto dto);
}

public class CategoryService : ICategoryService
{
    private readonly ICategoryRepository _repo;
    private readonly IValidator<SaveCategoryDto> _validator;

    public CategoryService(ICategoryRepository repo, IValidator<SaveCategoryDto> validator)
    {
        _repo = repo;
        _validator = validator;
    }

    public async Task<List<CategoryDto>> ListAsync() => (await _repo.ListAsync()).Select(CategoryMapper.ToDto).ToList();

    public async Task<CategoryDto> GetAsync(int id)
    {
        var c = await _repo.GetAsync(id) ?? throw new NotFoundException($"Category {id} was not found.");
        return CategoryMapper.ToDto(c);
    }

    public async Task<CategoryDto> CreateAsync(SaveCategoryDto dto)
    {
        await _validator.ValidateAndThrowAsync(dto);
        if (await _repo.GetByCodeAsync(dto.Code.Trim().ToUpperInvariant()) != null)
            throw new ConflictException($"Category code {dto.Code} is already in use.");
        var c = new Category();
        CategoryMapper.Apply(c, dto);
        await _repo.AddAsync(c);
        await _repo.SaveAsync();
        return CategoryMapper.ToDto(c);
    }

    public async Task<CategoryDto> UpdateAsync(int id, SaveCategoryDto dto)
    {
        await _validator.ValidateAndThrowAsync(dto);
        var c = await _repo.GetAsync(id) ?? throw new NotFoundException($"Category {id} was not found.");
        CategoryMapper.Apply(c, dto);
        await _repo.SaveAsync();
        return CategoryMapper.ToDto(c);
    }
}

public interface IProductService
{
    Task<PagedResult<ProductDto>> SearchAsync(string? query, int? categoryId, bool includeInactive, int page, int pageSize);
    Task<ProductDto> GetAsync(int id);
    Task<ProductDto> CreateAsync(SaveProductDto dto);
    Task<ProductDto> UpdateAsync(int id, SaveProductDto dto);
    Task DeactivateAsync(int id);
}

public class ProductService : IProductService
{
    private readonly IProductRepository _products;
    private readonly ICategoryRepository _categories;
    private readonly IValidator<SaveProductDto> _validator;

    public ProductService(IProductRepository products, ICategoryRepository categories, IValidator<SaveProductDto> validator)
    {
        _products = products;
        _categories = categories;
        _validator = validator;
    }

    public async Task<PagedResult<ProductDto>> SearchAsync(string? query, int? categoryId, bool includeInactive, int page, int pageSize)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var (items, total) = await _products.SearchAsync(query, categoryId, includeInactive, page, pageSize);
        return new PagedResult<ProductDto>(items.Select(ProductMapper.ToDto).ToList(), total, page, pageSize);
    }

    public async Task<ProductDto> GetAsync(int id)
    {
        var p = await _products.GetAsync(id) ?? throw new NotFoundException($"Product {id} was not found.");
        return ProductMapper.ToDto(p);
    }

    public async Task<ProductDto> CreateAsync(SaveProductDto dto)
    {
        await _validator.ValidateAndThrowAsync(dto);
        if (await _categories.GetAsync(dto.CategoryId) == null)
            throw new NotFoundException($"Category {dto.CategoryId} was not found.");
        if (await _products.GetBySkuAsync(dto.Sku.Trim().ToUpperInvariant()) != null)
            throw new ConflictException($"SKU {dto.Sku} already exists.");
        var p = new Product { CreatedUtc = DateTime.UtcNow, IsActive = true };
        ProductMapper.Apply(p, dto);
        await _products.AddAsync(p);
        await _products.SaveAsync();
        return await GetAsync(p.Id);
    }

    public async Task<ProductDto> UpdateAsync(int id, SaveProductDto dto)
    {
        await _validator.ValidateAndThrowAsync(dto);
        var p = await _products.GetAsync(id) ?? throw new NotFoundException($"Product {id} was not found.");
        if (await _categories.GetAsync(dto.CategoryId) == null)
            throw new NotFoundException($"Category {dto.CategoryId} was not found.");
        ProductMapper.Apply(p, dto);
        await _products.SaveAsync();
        return await GetAsync(id);
    }

    public async Task DeactivateAsync(int id)
    {
        var p = await _products.GetAsync(id) ?? throw new NotFoundException($"Product {id} was not found.");
        p.IsActive = false;
        await _products.SaveAsync();
    }
}

public interface IStoreService
{
    Task<List<StoreDto>> ListAsync();
    Task<StoreDto> GetAsync(int id);
    Task<StoreDto> CreateAsync(SaveStoreDto dto);
    Task<StoreDto> UpdateAsync(int id, SaveStoreDto dto);
}

public class StoreService : IStoreService
{
    private readonly IStoreRepository _repo;
    private readonly IValidator<SaveStoreDto> _validator;

    public StoreService(IStoreRepository repo, IValidator<SaveStoreDto> validator)
    {
        _repo = repo;
        _validator = validator;
    }

    public async Task<List<StoreDto>> ListAsync() => (await _repo.ListAsync()).Select(StoreMapper.ToDto).ToList();

    public async Task<StoreDto> GetAsync(int id)
    {
        var s = await _repo.GetAsync(id) ?? throw new NotFoundException($"Store {id} was not found.");
        return StoreMapper.ToDto(s);
    }

    public async Task<StoreDto> CreateAsync(SaveStoreDto dto)
    {
        await _validator.ValidateAndThrowAsync(dto);
        if (await _repo.GetByCodeAsync(dto.Code.Trim().ToUpperInvariant()) != null)
            throw new ConflictException($"Store code {dto.Code} is already in use.");
        var s = new Store();
        StoreMapper.Apply(s, dto);
        await _repo.AddAsync(s);
        await _repo.SaveAsync();
        return StoreMapper.ToDto(s);
    }

    public async Task<StoreDto> UpdateAsync(int id, SaveStoreDto dto)
    {
        await _validator.ValidateAndThrowAsync(dto);
        var s = await _repo.GetAsync(id) ?? throw new NotFoundException($"Store {id} was not found.");
        StoreMapper.Apply(s, dto);
        await _repo.SaveAsync();
        return StoreMapper.ToDto(s);
    }
}

public interface IStockService
{
    Task<List<StockLevelDto>> ListAsync(int? storeId, int? productId, bool lowOnly);
    Task<StockLevelDto> GetAsync(int id);
    Task<StockLevelDto> AdjustAsync(StockAdjustDto dto);
    Task<StockLevelDto> SetAsync(int id, SetStockDto dto);
}

public class StockService : IStockService
{
    private readonly IStockRepository _stock;
    private readonly IStoreRepository _stores;
    private readonly IProductRepository _products;
    private readonly IValidator<StockAdjustDto> _adjustValidator;
    private readonly IValidator<SetStockDto> _setValidator;

    public StockService(IStockRepository stock, IStoreRepository stores, IProductRepository products,
        IValidator<StockAdjustDto> adjustValidator, IValidator<SetStockDto> setValidator)
    {
        _stock = stock;
        _stores = stores;
        _products = products;
        _adjustValidator = adjustValidator;
        _setValidator = setValidator;
    }

    public async Task<List<StockLevelDto>> ListAsync(int? storeId, int? productId, bool lowOnly) =>
        (await _stock.ListAsync(storeId, productId, lowOnly)).Select(StockMapper.ToDto).ToList();

    public async Task<StockLevelDto> GetAsync(int id)
    {
        var s = await _stock.GetAsync(id) ?? throw new NotFoundException($"Stock row {id} was not found.");
        return StockMapper.ToDto(s);
    }

    public async Task<StockLevelDto> AdjustAsync(StockAdjustDto dto)
    {
        await _adjustValidator.ValidateAndThrowAsync(dto);
        if (await _stores.GetAsync(dto.StoreId) == null) throw new NotFoundException($"Store {dto.StoreId} was not found.");
        if (await _products.GetAsync(dto.ProductId) == null) throw new NotFoundException($"Product {dto.ProductId} was not found.");

        var stock = await _stock.GetForStoreProductAsync(dto.StoreId, dto.ProductId);
        if (stock == null)
        {
            if (dto.Delta < 0) throw new BusinessRuleException("There is no stock of this product in this store to remove.");
            stock = new StockLevel { StoreId = dto.StoreId, ProductId = dto.ProductId, Quantity = 0, ReorderLevel = 2 };
            await _stock.AddAsync(stock);
        }

        var newQuantity = stock.Quantity + dto.Delta;
        if (newQuantity < 0) throw new BusinessRuleException("Stock cannot go below zero.");
        stock.Quantity = newQuantity;
        stock.UpdatedUtc = DateTime.UtcNow;
        await _stock.SaveAsync();
        return await GetAsync(stock.Id);
    }

    public async Task<StockLevelDto> SetAsync(int id, SetStockDto dto)
    {
        await _setValidator.ValidateAndThrowAsync(dto);
        var stock = await _stock.GetAsync(id) ?? throw new NotFoundException($"Stock row {id} was not found.");
        stock.Quantity = dto.Quantity;
        stock.ReorderLevel = dto.ReorderLevel;
        stock.UpdatedUtc = DateTime.UtcNow;
        await _stock.SaveAsync();
        return StockMapper.ToDto(stock);
    }
}
