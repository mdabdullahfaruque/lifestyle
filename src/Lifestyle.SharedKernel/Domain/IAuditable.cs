namespace Lifestyle.SharedKernel.Domain;

/// <summary>Audit columns, populated by the SaveChanges interceptor — never set by hand.</summary>
public interface IAuditable
{
    DateTimeOffset CreatedAt { get; set; }
    Guid? CreatedBy { get; set; }
    DateTimeOffset? UpdatedAt { get; set; }
    Guid? UpdatedBy { get; set; }
}

/// <summary>
/// Opt in only where history must survive a delete (products, vendors, reviews).
/// Orders and ledger entries are never deleted and must not implement this.
/// </summary>
public interface ISoftDeletable
{
    DateTimeOffset? DeletedAt { get; set; }
    Guid? DeletedBy { get; set; }
}
