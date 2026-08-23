using Lifestyle.SharedKernel.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Lifestyle.SharedKernel.Http;

/// <summary>
/// The single place a <see cref="Result"/> becomes an HTTP response. Every failure leaves as
/// RFC 9457 <c>application/problem+json</c> with a stable <c>type</c> URI built from the error code
/// (docs/04 §3.5), so clients can branch on the code and never on the message text.
/// </summary>
public static class ResultExtensions
{
    private const string ProblemTypeBase = "https://errors.lifestyle.dev/";

    public static IResult ToHttpResult(this Result result) =>
        result.IsSuccess ? Microsoft.AspNetCore.Http.Results.NoContent() : Problem(result.Error);

    public static IResult ToHttpResult<T>(this Result<T> result) =>
        result.IsSuccess ? Microsoft.AspNetCore.Http.Results.Ok(result.Value) : Problem(result.Error);

    /// <summary>201 with a Location header built from the created value.</summary>
    public static IResult ToCreated<T>(this Result<T> result, Func<T, string> location) =>
        result.IsSuccess
            ? Microsoft.AspNetCore.Http.Results.Created(location(result.Value), result.Value)
            : Problem(result.Error);

    public static IResult ToAccepted<T>(this Result<T> result) =>
        result.IsSuccess
            ? Microsoft.AspNetCore.Http.Results.Accepted(value: result.Value)
            : Problem(result.Error);

    public static IResult Problem(Error error)
    {
        var status = StatusFor(error.Type);

        if (error.Type == ErrorType.Validation && error.FieldErrors is { Count: > 0 })
        {
            return Microsoft.AspNetCore.Http.Results.ValidationProblem(
                errors: error.FieldErrors.ToDictionary(e => e.Key, e => e.Value, StringComparer.Ordinal),
                detail: error.Message,
                type: ProblemTypeBase + error.Code,
                title: "Validation failed",
                extensions: new Dictionary<string, object?>(StringComparer.Ordinal) { ["code"] = error.Code });
        }

        return Microsoft.AspNetCore.Http.Results.Problem(new ProblemDetails
        {
            Status = status,
            Title = TitleFor(error.Type),
            Detail = error.Message,
            Type = ProblemTypeBase + error.Code,
            Extensions = { ["code"] = error.Code }
        });
    }

    public static int StatusFor(ErrorType type) => type switch
    {
        ErrorType.Validation => StatusCodes.Status400BadRequest,
        ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
        ErrorType.Forbidden => StatusCodes.Status403Forbidden,
        ErrorType.NotFound => StatusCodes.Status404NotFound,
        ErrorType.Conflict => StatusCodes.Status409Conflict,
        _ => StatusCodes.Status500InternalServerError
    };

    private static string TitleFor(ErrorType type) => type switch
    {
        ErrorType.Validation => "Validation failed",
        ErrorType.Unauthorized => "Authentication required",
        ErrorType.Forbidden => "Forbidden",
        ErrorType.NotFound => "Not found",
        ErrorType.Conflict => "Conflict",
        _ => "An unexpected error occurred"
    };
}
