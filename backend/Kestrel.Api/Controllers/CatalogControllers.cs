using Kestrel.Api.Dtos;
using Kestrel.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Kestrel.Api.Controllers;

[ApiController]
[Route("api/categories")]
public class CategoriesController : ControllerBase
{
    private readonly ICategoryService _service;
    public CategoriesController(ICategoryService service) => _service = service;

    [HttpGet]
    public async Task<ActionResult<List<CategoryDto>>> List() => Ok(await _service.ListAsync());

    [HttpGet("{id:int}")]
    public async Task<ActionResult<CategoryDto>> Get(int id) => Ok(await _service.GetAsync(id));

    [HttpPost]
    public async Task<ActionResult<CategoryDto>> Create(SaveCategoryDto dto)
    {
        var created = await _service.CreateAsync(dto);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<CategoryDto>> Update(int id, SaveCategoryDto dto) => Ok(await _service.UpdateAsync(id, dto));
}

[ApiController]
[Route("api/products")]
public class ProductsController : ControllerBase
{
    private readonly IProductService _service;
    public ProductsController(IProductService service) => _service = service;

    [HttpGet]
    public async Task<ActionResult<PagedResult<ProductDto>>> Search([FromQuery] string? q, [FromQuery] int? categoryId,
        [FromQuery] bool includeInactive = false, [FromQuery] int page = 1, [FromQuery] int pageSize = 25) =>
        Ok(await _service.SearchAsync(q, categoryId, includeInactive, page, pageSize));

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ProductDto>> Get(int id) => Ok(await _service.GetAsync(id));

    [HttpPost]
    public async Task<ActionResult<ProductDto>> Create(SaveProductDto dto)
    {
        var created = await _service.CreateAsync(dto);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<ProductDto>> Update(int id, SaveProductDto dto) => Ok(await _service.UpdateAsync(id, dto));

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Deactivate(int id)
    {
        await _service.DeactivateAsync(id);
        return NoContent();
    }
}

[ApiController]
[Route("api/stores")]
public class StoresController : ControllerBase
{
    private readonly IStoreService _service;
    private readonly IStockService _stock;

    public StoresController(IStoreService service, IStockService stock)
    {
        _service = service;
        _stock = stock;
    }

    [HttpGet]
    public async Task<ActionResult<List<StoreDto>>> List() => Ok(await _service.ListAsync());

    [HttpGet("{id:int}")]
    public async Task<ActionResult<StoreDto>> Get(int id) => Ok(await _service.GetAsync(id));

    [HttpPost]
    public async Task<ActionResult<StoreDto>> Create(SaveStoreDto dto)
    {
        var created = await _service.CreateAsync(dto);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<StoreDto>> Update(int id, SaveStoreDto dto) => Ok(await _service.UpdateAsync(id, dto));

    [HttpGet("{id:int}/stock")]
    public async Task<ActionResult<List<StockLevelDto>>> StockForStore(int id, [FromQuery] bool lowOnly = false)
    {
        await _service.GetAsync(id);
        return Ok(await _stock.ListAsync(id, null, lowOnly));
    }
}

[ApiController]
[Route("api/stock")]
public class StockController : ControllerBase
{
    private readonly IStockService _service;
    public StockController(IStockService service) => _service = service;

    [HttpGet]
    public async Task<ActionResult<List<StockLevelDto>>> List([FromQuery] int? storeId, [FromQuery] int? productId, [FromQuery] bool lowOnly = false) =>
        Ok(await _service.ListAsync(storeId, productId, lowOnly));

    [HttpGet("{id:int}")]
    public async Task<ActionResult<StockLevelDto>> Get(int id) => Ok(await _service.GetAsync(id));

    [HttpPost("adjust")]
    public async Task<ActionResult<StockLevelDto>> Adjust(StockAdjustDto dto) => Ok(await _service.AdjustAsync(dto));

    [HttpPut("{id:int}")]
    public async Task<ActionResult<StockLevelDto>> Set(int id, SetStockDto dto) => Ok(await _service.SetAsync(id, dto));
}
