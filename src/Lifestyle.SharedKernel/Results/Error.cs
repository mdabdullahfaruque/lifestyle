namespace Lifestyle.SharedKernel.Results;

/// <summary>Determines the HTTP status the API layer maps to. Nothing else.</summary>
public enum ErrorType
{
    Validation,
    NotFound,
    Conflict,
    Forbidden,
    Unauthorized,
    Failure
}

/// <summary>
/// An expected failure. <see cref="Code"/> is a stable, namespaced, machine-readable string
/// (<c>catalog.category_not_found</c>) — the frontend switches on the code, never the message.
/// </summary>
public sealed record Error(string Code, string Message, ErrorType Type)
{
    public static readonly Error None = new(string.Empty, string.Empty, ErrorType.Failure);

    /// <summary>Per-field messages, set only for <see cref="ErrorType.Validation"/>.</summary>
    public IReadOnlyDictionary<string, string[]>? FieldErrors { get; init; }

    public static Error Validation(string code, string message) => new(code, message, ErrorType.Validation);

    public static Error Validation(IReadOnlyDictionary<string, string[]> fieldErrors) =>
        new("validation.failed", "One or more validation errors occurred.", ErrorType.Validation)
        {
            FieldErrors = fieldErrors
        };

    public static Error NotFound(string code, string? message = null) =>
        new(code, message ?? "The requested resource was not found.", ErrorType.NotFound);

    public static Error Conflict(string code, string message) => new(code, message, ErrorType.Conflict);

    public static Error Forbidden(string code, string? message = null) =>
        new(code, message ?? "You do not have permission to perform this action.", ErrorType.Forbidden);

    public static Error Unauthorized(string code, string? message = null) =>
        new(code, message ?? "Authentication is required.", ErrorType.Unauthorized);

    public static Error Failure(string code, string message) => new(code, message, ErrorType.Failure);
}
