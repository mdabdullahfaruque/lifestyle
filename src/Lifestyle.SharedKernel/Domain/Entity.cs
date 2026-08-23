namespace Lifestyle.SharedKernel.Domain;

/// <summary>
/// Base for every persisted entity. Identity is the <see cref="Id"/>, never reference equality.
/// </summary>
public abstract class Entity
{
    /// <summary>
    /// UUID v7 — time-ordered, so it behaves like a sequential key in the index while staying
    /// opaque and safe to expose. Assigned in application code, never by the database.
    /// </summary>
    public Guid Id { get; protected set; } = Guid.CreateVersion7();

    public override bool Equals(object? obj) =>
        obj is Entity other && other.GetType() == GetType() && other.Id == Id && Id != Guid.Empty;

    public override int GetHashCode() => HashCode.Combine(GetType(), Id);
}
