namespace Lifestyle.SharedKernel.Abstractions;

/// <summary>
/// The only way to read the current time. <c>DateTime.UtcNow</c> in domain or feature code is an
/// architecture-test failure — it makes tests non-deterministic.
/// </summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
