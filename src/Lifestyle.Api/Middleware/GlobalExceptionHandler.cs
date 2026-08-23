using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Lifestyle.Api.Middleware;

/// <summary>
/// The last line of defence. Expected failures never reach here — handlers return
/// <c>Result</c> for those (docs/04 §3.5). What arrives is a bug or an infrastructure fault, so it
/// is logged in full and reported to the client as an opaque 500 with a trace id.
/// <para>
/// The one exception is a database constraint violation, which is translated into the status it
/// deserves. A unique-index race is a real, expected outcome under concurrency; returning 500 for
/// it would be wrong.
/// </para>
/// </summary>
internal sealed class GlobalExceptionHandler(
    IHostEnvironment environment,
    ILogger<GlobalExceptionHandler> logger)
    : IExceptionHandler
{
    private const string ProblemTypeBase = "https://errors.lifestyle.dev/";

    public async ValueTask<bool> TryHandleAsync(
        HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is DbUpdateException dbUpdate && dbUpdate.InnerException is PostgresException pg)
        {
            var (status, code, detail) = Translate(pg);

            logger.LogWarning(dbUpdate,
                "Database constraint {SqlState} on {Method} {Path} returned {Status}.",
                pg.SqlState, context.Request.Method, context.Request.Path, status);

            await WriteAsync(context, status, TitleFor(status), detail, code, cancellationToken);
            return true;
        }

        logger.LogError(exception,
            "Unhandled exception on {Method} {Path}. TraceId={TraceId}",
            context.Request.Method, context.Request.Path, context.TraceIdentifier);

        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status500InternalServerError,
            Title = "An unexpected error occurred",
            Detail = "The request could not be completed. Quote the trace id if you contact support.",
            Type = ProblemTypeBase + "internal_error",
            Extensions =
            {
                ["code"] = "internal_error",
                ["traceId"] = context.TraceIdentifier
            }
        };

        // Stack traces only in development. In production they are an information leak.
        if (environment.IsDevelopment())
        {
            problem.Extensions["exception"] = exception.GetType().FullName;
            problem.Extensions["message"] = exception.Message;
            problem.Extensions["stackTrace"] = exception.StackTrace;
        }

        context.Response.StatusCode = problem.Status.Value;
        await context.Response.WriteAsJsonAsync(problem, cancellationToken);
        return true;
    }

    /// <summary>
    /// Maps PostgreSQL SQLSTATE codes to sensible HTTP statuses. Handlers should check first, so
    /// reaching here usually means a race — two requests passing the same check at once.
    /// </summary>
    private static (int Status, string Code, string Detail) Translate(PostgresException pg) => pg.SqlState switch
    {
        PostgresErrorCodes.UniqueViolation =>
            (StatusCodes.Status409Conflict, "conflict.duplicate",
                "That value is already in use. Refresh and try again."),

        PostgresErrorCodes.ForeignKeyViolation =>
            (StatusCodes.Status400BadRequest, "validation.reference_missing",
                "The request refers to something that no longer exists."),

        PostgresErrorCodes.NotNullViolation =>
            (StatusCodes.Status400BadRequest, "validation.required_field",
                $"A required value was missing{(pg.ColumnName is null ? "" : $": {pg.ColumnName}")}."),

        PostgresErrorCodes.CheckViolation =>
            (StatusCodes.Status400BadRequest, "validation.constraint",
                "One of the supplied values is not allowed."),

        // 40001/40P01: a serialisation failure or deadlock. Retrying genuinely may succeed.
        PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected =>
            (StatusCodes.Status409Conflict, "conflict.concurrent_update",
                "Another change landed at the same time. Try again."),

        _ => (StatusCodes.Status500InternalServerError, "internal_error",
            "The request could not be completed.")
    };

    private static string TitleFor(int status) => status switch
    {
        StatusCodes.Status400BadRequest => "Validation failed",
        StatusCodes.Status409Conflict => "Conflict",
        _ => "An unexpected error occurred"
    };

    private static async Task WriteAsync(
        HttpContext context, int status, string title, string detail, string code, CancellationToken ct)
    {
        context.Response.StatusCode = status;

        await context.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = detail,
            Type = ProblemTypeBase + code,
            Extensions =
            {
                ["code"] = code,
                ["traceId"] = context.TraceIdentifier
            }
        }, ct);
    }
}
