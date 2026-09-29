using dEngage.Loyalty.Shared;

namespace dEngage.Loyalty.Ledger;

public interface IRefundService
{
    Task<Result> ProcessRefundAsync(
        string tenantId,
        string refundEventId,
        string originalEventId,
        decimal refundRatio,
        CancellationToken ct = default);
}
