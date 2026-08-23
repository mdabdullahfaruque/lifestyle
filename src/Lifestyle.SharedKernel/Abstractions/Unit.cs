namespace Lifestyle.SharedKernel.Abstractions;

/// <summary>
/// The request type for a handler that takes no input. Keeps every use case on the same
/// <see cref="IHandler{TRequest,TResponse}"/> shape instead of adding a second interface.
/// </summary>
public sealed record Unit
{
    public static Unit Value { get; } = new();
}
