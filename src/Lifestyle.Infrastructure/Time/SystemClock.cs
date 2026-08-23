using Lifestyle.SharedKernel.Abstractions;

namespace Lifestyle.Infrastructure.Time;

/// <summary>
/// The one place <c>DateTimeOffset.UtcNow</c> is allowed to be called. Everything else takes
/// <see cref="IClock"/>, which is what makes time-dependent behaviour testable.
/// </summary>
internal sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
