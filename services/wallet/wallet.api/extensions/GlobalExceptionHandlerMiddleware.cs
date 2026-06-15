using System.Diagnostics;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;

namespace wallet.api.extensions;

public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var traceId = Activity.Current?.Id ?? httpContext.TraceIdentifier;
        logger.LogError(exception,
            "An unhandled exception occurred during request {TraceId}. Machine: {MachineName}",
            traceId,
            Environment.MachineName);

        var (statusCode, message, errorDetails) = MapException(exception);
        var response = ApiResponse<object>.Fail(message: message, errors: [errorDetails], traceId: traceId);

        // We still set the correct HTTP Status Code so infrastructure (like Nginx) behaves correctly
        httpContext.Response.StatusCode = statusCode;
        httpContext.Response.ContentType = "application/json";

        await httpContext.Response.WriteAsJsonAsync(response, cancellationToken);
        return true;
    }

    private static (int StatusCode, string Title, string Detail) MapException(Exception exception)
    {
        return exception switch
        {
            // Handle Race Conditions (e.g., concurrent wallet debits)
            DbUpdateConcurrencyException => (StatusCodes.Status409Conflict,
                "State Conflict", "The request could not be completed due to a conflict with the current state of the resource. Please retry the operation."
            ),

            // Handle Domain/Business Logic Violations
            InvalidOperationException ex => (StatusCodes.Status400BadRequest,
                "Operation Failed", ex.Message
            ),

            // Handle standard bad requests (e.g., malformed JSON)
            BadHttpRequestException => (StatusCodes.Status400BadRequest,
                "Bad Request", "The request could not be processed due to invalid syntax or missing data. Please verify the payload format and try again."
            ),

            // Default Fallback for truly unhandled system crashes
            _ => (StatusCodes.Status500InternalServerError,
                "Internal Server Error", "An unexpected error occurred while processing your request. Please try again later, or contact support if the issue persists."
            )
        };
    }
}

public sealed class ApiResponse<T>
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public T? Data { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IEnumerable<string>? Errors { get; init; }
    public string TraceId { get; init; } = string.Empty;
    public static ApiResponse<T> Fail(string message, IEnumerable<string>? errors = null, string traceId = "") =>
        new() { Success = false, Message = message, Errors = errors, TraceId = traceId };
}