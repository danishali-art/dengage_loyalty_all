using dEngage.Loyalty.Api.Framework.Data;
using dEngage.Loyalty.Api.Framework.ErrorHandling;
using dEngage.Loyalty.Api.Framework.Pagination;
using dEngage.Loyalty.Schema;
using dEngage.Loyalty.Shared;
using Microsoft.EntityFrameworkCore;
using ComplaintEntity = dEngage.Loyalty.Schema.Entities.Complaint;

namespace dEngage.Loyalty.Api.Complaints;

public sealed record ComplaintListFilter(string? Status, Guid? ProgramId);

public interface IComplaintsAppService
{
    Task<PagedResult<ComplaintResponse>> ListAsync(string tenantId, ComplaintListFilter filter, PageRequest page, CancellationToken ct);
    Task<ComplaintResponse> GetAsync(string tenantId, Guid complaintId, CancellationToken ct);
    Task<ComplaintResponse> CreateAsync(string tenantId, CreateComplaintRequest request, CancellationToken ct);
    Task<ComplaintResponse> UpdateStatusAsync(string tenantId, Guid complaintId, UpdateComplaintStatusRequest request, CancellationToken ct);
    Task<ComplaintSummaryResponse> SummaryAsync(string tenantId, Guid? programId, CancellationToken ct);
}

public sealed class ComplaintsAppService(IRepository<ComplaintEntity> repository, ITenantSlugResolver tenantSlugResolver) : IComplaintsAppService
{
    public async Task<PagedResult<ComplaintResponse>> ListAsync(string tenantId, ComplaintListFilter filter, PageRequest page, CancellationToken ct)
    {
        var query = await repository.Query(tenantId, ct);
        if (filter.Status is not null) query = query.Where(c => c.Status == filter.Status);
        if (filter.ProgramId is not null) query = query.Where(c => c.ProgramId == filter.ProgramId);
        query = query.OrderByDescending(c => c.CreatedAt);

        var total = await query.CountAsync(ct);
        var entities = await query.Skip((page.Page - 1) * page.PageSize).Take(page.PageSize).ToListAsync(ct);

        return new PagedResult<ComplaintResponse>
        {
            Data = entities.Select(ToResponse).ToList(),
            Page = page.Page,
            PageSize = page.PageSize,
            Total = total
        };
    }

    public async Task<ComplaintResponse> GetAsync(string tenantId, Guid complaintId, CancellationToken ct)
    {
        var entity = await repository.FindAsync(tenantId, complaintId, ct)
            ?? throw new NotFoundApiException($"Complaint '{complaintId}'");
        return ToResponse(entity);
    }

    public async Task<ComplaintResponse> CreateAsync(string tenantId, CreateComplaintRequest request, CancellationToken ct)
    {
        var tenantGuid = await tenantSlugResolver.ResolveAsync(tenantId, ct);
        var entity = new ComplaintEntity
        {
            Id = Guid.NewGuid(),
            TenantId = tenantGuid,
            ProgramId = request.ProgramId,
            CustomerKey = request.CustomerKey,
            Subject = request.Subject,
            Description = request.Description,
            Status = ComplaintStatus.Open,
            CreatedAt = DateTime.UtcNow
        };

        await repository.AddAsync(entity, ct);
        await repository.SaveChangesAsync(ct);
        return ToResponse(entity);
    }

    public async Task<ComplaintResponse> UpdateStatusAsync(string tenantId, Guid complaintId, UpdateComplaintStatusRequest request, CancellationToken ct)
    {
        var entity = await repository.FindAsync(tenantId, complaintId, ct)
            ?? throw new NotFoundApiException($"Complaint '{complaintId}'");

        entity.Status = request.Status;
        entity.ResolvedAt = request.Status == ComplaintStatus.Resolved ? DateTime.UtcNow : null;

        await repository.SaveChangesAsync(ct);
        return ToResponse(entity);
    }

    public async Task<ComplaintSummaryResponse> SummaryAsync(string tenantId, Guid? programId, CancellationToken ct)
    {
        var query = await repository.Query(tenantId, ct);
        if (programId is not null) query = query.Where(c => c.ProgramId == programId);

        var counts = await query.GroupBy(c => c.Status).Select(g => new { Status = g.Key, Count = g.Count() }).ToListAsync(ct);
        int CountOf(string status) => counts.FirstOrDefault(c => c.Status == status)?.Count ?? 0;

        var open = CountOf(ComplaintStatus.Open);
        var inProgress = CountOf(ComplaintStatus.InProgress);
        var resolved = CountOf(ComplaintStatus.Resolved);
        return new ComplaintSummaryResponse(open, inProgress, resolved, open + inProgress + resolved);
    }

    private static ComplaintResponse ToResponse(ComplaintEntity c) => new(
        c.Id, c.ProgramId, c.CustomerKey, c.Subject, c.Description, c.Status, c.CreatedAt, c.ResolvedAt);
}
