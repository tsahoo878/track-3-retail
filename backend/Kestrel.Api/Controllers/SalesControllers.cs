using Kestrel.Api.Dtos;
using Kestrel.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Kestrel.Api.Controllers;

[ApiController]
[Route("api/customers")]
public class CustomersController : ControllerBase
{
    private readonly ICustomerService _service;
    private readonly IOrderService _orders;

    public CustomersController(ICustomerService service, IOrderService orders)
    {
        _service = service;
        _orders = orders;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<CustomerDto>>> Search([FromQuery] string? q, [FromQuery] int page = 1, [FromQuery] int pageSize = 25) =>
        Ok(await _service.SearchAsync(q, page, pageSize));

    [HttpGet("summary")]
    public async Task<ActionResult<CustomerSummaryDto>> Summary() => Ok(await _service.GetSummaryAsync());

    [HttpGet("{id:int}")]
    public async Task<ActionResult<CustomerDto>> Get(int id) => Ok(await _service.GetAsync(id));

    [HttpPost]
    public async Task<ActionResult<CustomerDto>> Create(SaveCustomerDto dto)
    {
        var created = await _service.CreateAsync(dto);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<CustomerDto>> Update(int id, SaveCustomerDto dto) => Ok(await _service.UpdateAsync(id, dto));

    [HttpGet("{id:int}/orders")]
    public async Task<ActionResult<List<OrderListItemDto>>> Orders(int id) => Ok(await _orders.ListForCustomerAsync(id));
}

[ApiController]
[Route("api/orders")]
public class OrdersController : ControllerBase
{
    private readonly IOrderService _service;
    private readonly IPaymentService _payments;

    public OrdersController(IOrderService service, IPaymentService payments)
    {
        _service = service;
        _payments = payments;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<OrderListItemDto>>> Search([FromQuery] int? storeId, [FromQuery] int? customerId,
        [FromQuery] string? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 25) =>
        Ok(await _service.SearchAsync(storeId, customerId, status, page, pageSize));

    [HttpGet("{id:int}")]
    public async Task<ActionResult<OrderDetailDto>> Get(int id) => Ok(await _service.GetAsync(id));

    [HttpPost]
    public async Task<ActionResult<OrderDetailDto>> Create(CreateOrderDto dto)
    {
        var created = await _service.CreateAsync(dto);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPost("{id:int}/status")]
    public async Task<ActionResult<OrderDetailDto>> ChangeStatus(int id, ChangeStatusDto dto) => Ok(await _service.ChangeStatusAsync(id, dto));

    [HttpGet("{id:int}/history")]
    public async Task<ActionResult<List<StatusHistoryDto>>> History(int id) => Ok(await _service.GetHistoryAsync(id));

    [HttpGet("{id:int}/notes")]
    public async Task<ActionResult<List<OrderNoteDto>>> Notes(int id) => Ok(await _service.GetNotesAsync(id));

    [HttpPost("{id:int}/notes")]
    public async Task<ActionResult<OrderNoteDto>> AddNote(int id, AddNoteDto dto) => Ok(await _service.AddNoteAsync(id, dto));

    [HttpGet("{id:int}/payments")]
    public async Task<ActionResult<List<PaymentDto>>> Payments(int id) => Ok(await _payments.ListForOrderAsync(id));

    [HttpPost("{id:int}/payments")]
    public async Task<ActionResult<PaymentDto>> AddPayment(int id, CreatePaymentDto dto)
    {
        var created = await _payments.AddAsync(id, dto);
        return CreatedAtAction("GetPayment", "Payments", new { id = created.Id }, created);
    }
}

[ApiController]
[Route("api/payments")]
public class PaymentsController : ControllerBase
{
    private readonly IPaymentService _service;
    public PaymentsController(IPaymentService service) => _service = service;

    [HttpGet("{id:int}", Name = "GetPayment")]
    public async Task<ActionResult<PaymentDto>> GetPayment(int id) => Ok(await _service.GetAsync(id));
}

[ApiController]
[Route("api/reports")]
public class ReportsController : ControllerBase
{
    private readonly IReportService _service;
    private readonly IStockService _stock;

    public ReportsController(IReportService service, IStockService stock)
    {
        _service = service;
        _stock = stock;
    }

    [HttpGet("daily-sales")]
    public async Task<ActionResult<DailySalesDto>> DailySales([FromQuery] DateOnly? date) => Ok(await _service.GetDailySalesAsync(date));

    [HttpGet("daily-sales/range")]
    public async Task<ActionResult<List<DailySalesSummaryDto>>> DailySalesRange([FromQuery] DateOnly from, [FromQuery] DateOnly to) =>
        Ok(await _service.GetDailySalesRangeAsync(from, to));

    [HttpGet("top-products")]
    public async Task<ActionResult<List<TopProductDto>>> TopProducts([FromQuery] DateOnly from, [FromQuery] DateOnly to, [FromQuery] int take = 10) =>
        Ok(await _service.GetTopProductsAsync(from, to, take));

    [HttpGet("low-stock")]
    public async Task<ActionResult<List<StockLevelDto>>> LowStock([FromQuery] int? storeId) =>
        Ok(await _stock.ListAsync(storeId, null, true));
}

[ApiController]
[Route("api/imports")]
public class ImportsController : ControllerBase
{
    private readonly IStockFileImportService _service;
    public ImportsController(IStockFileImportService service) => _service = service;

    [HttpPost("stock-file")]
    [RequestSizeLimit(5_000_000)]
    public async Task<ActionResult<ImportBatchDetailDto>> UploadStockFile(IFormFile file)
    {
        if (file == null || file.Length == 0) return BadRequest(new ProblemDetails { Title = "No file uploaded", Status = 400 });
        await using var stream = file.OpenReadStream();
        return Ok(await _service.ImportAsync(stream, file.FileName));
    }

    [HttpGet]
    public async Task<ActionResult<List<ImportBatchDto>>> List() => Ok(await _service.ListAsync());

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ImportBatchDetailDto>> Get(int id) => Ok(await _service.GetAsync(id));
}

[ApiController]
[Route("api/users")]
public class UsersController : ControllerBase
{
    private readonly IUserService _service;
    public UsersController(IUserService service) => _service = service;

    [HttpGet]
    public async Task<ActionResult<List<UserDto>>> List() => Ok(await _service.ListAsync());

    [HttpGet("{id:int}")]
    public async Task<ActionResult<UserDto>> Get(int id) => Ok(await _service.GetAsync(id));
}

[ApiController]
[Route("api/health")]
public class HealthController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok(new { status = "ok", service = "kestrel" });
}
