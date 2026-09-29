namespace dEngage.Loyalty.Api.Complaints;

public sealed record CreateComplaintRequest(Guid? ProgramId, string? CustomerKey, string Subject, string? Description);
public sealed record UpdateComplaintStatusRequest(string Status);

public sealed record ComplaintResponse(
    Guid Id, Guid? ProgramId, string? CustomerKey, string Subject, string? Description,
    string Status, DateTime CreatedAt, DateTime? ResolvedAt);

public sealed record ComplaintSummaryResponse(int Open, int InProgress, int Resolved, int Total);
