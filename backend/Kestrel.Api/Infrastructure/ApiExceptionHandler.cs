using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Kestrel.Api.Infrastructure;

public class ApiExceptionHandler : IExceptionHandler
{
    private readonly ILogger<ApiExceptionHandler> _logger;

    public ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) => _logger = logger;

    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        ProblemDetails problem;
        switch (exception)
        {
            case ValidationException ve:
                var errors = ve.Errors
                    .GroupBy(e => e.PropertyName)
                    .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());
                problem = new ValidationProblemDetails(errors)
                {
                    Status = StatusCodes.Status400BadRequest,
                    Title = "One or more validation errors occurred."
                };
                break;
            case NotFoundException nf:
                problem = new ProblemDetails { Status = 404, Title = "Not found", Detail = nf.Message };
                break;
            case ConflictException ce:
                problem = new ProblemDetails { Status = 409, Title = "Conflict", Detail = ce.Message };
                break;
            case BusinessRuleException br:
                problem = new ProblemDetails { Status = 422, Title = "Business rule violated", Detail = br.Message };
                break;
            default:
                _logger.LogError(exception, "Unhandled exception for {Path}", context.Request.Path);
                problem = new ProblemDetails { Status = 500, Title = "Unexpected error" };
                break;
        }

        context.Response.StatusCode = problem.Status ?? 500;
        await context.Response.WriteAsJsonAsync(problem, (System.Text.Json.JsonSerializerOptions?)null, "application/problem+json", ct);
        return true;
    }
}
