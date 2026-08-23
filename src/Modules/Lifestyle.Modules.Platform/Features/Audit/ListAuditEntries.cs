using Lifestyle.Modules.Identity.Contracts;
using Lifestyle.Modules.Platform.Persistence;
using Lifestyle.SharedKernel.Abstractions;
using Lifestyle.SharedKernel.Http;
using Lifestyle.SharedKernel.Paging;
using Lifestyle.SharedKernel.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Lifestyle.Modules.Platform.Features.Audit;

public sealed record AuditEntryResponse(
    Guid Id,
    DateTimeOffset OccurredAt,
    string Action,
    string EntityType,
    Guid? EntityId,
    Guid? ActorUserId,
    Guid? ImpersonatedBy,
    Guid? VendorId,
    string? IpAddress,
    string? CorrelationId,
    string? Data);

internal static class ListAuditEntries
{
    public sealed record Request(
        string? Action,
        string? EntityType,
        Guid? EntityId,
        Guid? ActorUserId,
        DateTimeOffset? From,
        DateTimeOffset? To,
        int Page = 1,
        int PageSize = 50);

    internal sealed class Handler(IPlatformDbContext db)
        : IHandler<Request, Result<PagedResult<AuditEntryResponse>>>
    {
        public async Task<Result<PagedResult<AuditEntryResponse>>> Handle(Request request, CancellationToken ct)
        {
            var query = db.AuditEntries.AsNoTracking();

            if (!string.IsNullOrWhiteSpace(request.Action)) query = query.Where(a => a.Action == request.Action);
            if (!string.IsNullOrWhiteSpace(request.EntityType)) query = query.Where(a => a.EntityType == request.EntityType);
            if (request.EntityId is { } entityId) query = query.Where(a => a.EntityId == entityId);
            if (request.ActorUserId is { } actorId) query = query.Where(a => a.ActorUserId == actorId);
            if (request.From is { } from) query = query.Where(a => a.OccurredAt >= from);
            if (request.To is { } to) query = query.Where(a => a.OccurredAt <= to);

            var page = new PageRequest { Page = request.Page, PageSize = request.PageSize };
            var total = await query.CountAsync(ct);

            var items = await query
                .OrderByDescending(a => a.OccurredAt)
                .Skip(page.Skip)
                .Take(page.Size)
                .Select(a => new AuditEntryResponse(
                    a.Id, a.OccurredAt, a.Action, a.EntityType, a.EntityId, a.ActorUserId,
                    a.ImpersonatedBy, a.VendorId, a.IpAddress, a.CorrelationId, a.DataJson))
                .ToListAsync(ct);

            return PagedResult<AuditEntryResponse>.From(items, page, total);
        }
    }

    public static void Map(IEndpointRouteBuilder group) =>
        group.MapGet("/", async ([AsParameters] Request request, Handler handler, CancellationToken ct) =>
                (await handler.Handle(request, ct)).ToHttpResult())
            .WithName("ListAuditEntries")
            .WithSummary("Search the platform audit log.")
            .RequirePermission(Permissions.Platform.ReadAuditLog)
            .Produces<PagedResult<AuditEntryResponse>>();
}
