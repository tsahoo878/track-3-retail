using Kestrel.Api.Dtos;
using Kestrel.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Kestrel.Api.Controllers;

[ApiController]
[Route("api/returns")]
public class ReturnsController : ControllerBase
{
    private readonly IReturnService _service;
    public ReturnsController(IReturnService service) => _service = service;

    // The single "can this be returned?" call, also used by the chat team's pilot.
    [HttpGet("eligibility/{orderNumber}")]
    public async Task<ActionResult<EligibilityDto>> Eligibility(string orderNumber) => Ok(await _service.GetEligibilityAsync(orderNumber));

    [HttpPost]
    public async Task<ActionResult<ReturnDto>> Create(CreateReturnDto dto)
    {
        var created = await _service.CreateAsync(dto);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ReturnDto>> Get(int id) => Ok(await _service.GetAsync(id));

    [HttpPost("{id:int}/approve")]
    public async Task<ActionResult<ReturnDto>> Approve(int id, ApproveReturnDto dto) => Ok(await _service.ApproveAsync(id, dto.UserId));

    // DRAFT only; frees the quantity and refund cap the draft was holding.
    [HttpPost("{id:int}/reject")]
    public async Task<ActionResult<ReturnDto>> Reject(int id, RejectReturnDto dto) => Ok(await _service.RejectAsync(id, dto.UserId));

    [HttpGet("dashboard")]
    public async Task<ActionResult<ReturnsDashboardDto>> Dashboard([FromQuery] DateOnly? from, [FromQuery] DateOnly? to)
    {
        var end = to ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var start = from ?? end.AddDays(-90);
        return Ok(await _service.GetDashboardAsync(start, end));
    }
}
