using System.Net;
using System.Text.Json;
using TopUpService.Domain.Exceptions;

namespace TopUpService.API.Middlewares;

/// <summary>
/// Catches unhandled exceptions before they reach the client.
/// Returns a consistent JSON error envelope regardless of exception type.
/// </summary>
public sealed class GlobalExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionMiddleware> _logger;

    public GlobalExceptionMiddleware(RequestDelegate next, ILogger<GlobalExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (TopUpDomainException ex)
        {
            _logger.LogWarning(ex, "Domain rule violation: {Message}", ex.Message);
            await WriteErrorAsync(context, HttpStatusCode.UnprocessableEntity, "domain_error", ex.Message);
        }
        catch (OperationCanceledException)
        {
            // Client disconnected — no response needed.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error processing request {Method} {Path}",
                context.Request.Method, context.Request.Path);
            await WriteErrorAsync(context, HttpStatusCode.InternalServerError,
                "internal_error", "An unexpected error occurred.");
        }
    }

    private static async Task WriteErrorAsync(
        HttpContext context, HttpStatusCode status, string code, string message)
    {
        context.Response.ContentType = "application/json";
        context.Response.StatusCode = (int)status;

        var payload = JsonSerializer.Serialize(new { code, message });
        await context.Response.WriteAsync(payload);
    }
}
