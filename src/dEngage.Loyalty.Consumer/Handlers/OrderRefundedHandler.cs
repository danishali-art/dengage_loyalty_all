using System.Globalization;
using dEngage.Loyalty.Engine.Framework.Consumers;
using dEngage.Loyalty.Ledger;
using dEngage.Loyalty.Shared.Events;

namespace dEngage.Loyalty.Consumer.Handlers;

public class OrderRefundedHandler(IRefundService refundService) : IEventHandler
{
    public string? EventType => EventTypes.OrderRefunded;

    public async Task HandleAsync(EventEnvelope envelope, CancellationToken ct)
    {
        var data = envelope.Data;
        var originalEventId = data.GetProperty("original_event_id").GetString()!;

        decimal ratio;
        if (data.TryGetProperty("refund_ratio", out var ratioEl))
            ratio = decimal.Parse(ratioEl.GetString()!, CultureInfo.InvariantCulture);
        else
        {
            var refundAmount   = decimal.Parse(data.GetProperty("amount").GetString()!, CultureInfo.InvariantCulture);
            var originalAmount = decimal.Parse(data.GetProperty("original_amount").GetString()!, CultureInfo.InvariantCulture);
            ratio = originalAmount == 0 ? 1m : Math.Round(refundAmount / originalAmount, 6);
        }

        // A bad/corrupt payload cannot push the refund ratio above 1
        ratio = Math.Clamp(ratio, 0m, 1m);

        var result = await refundService.ProcessRefundAsync(
            tenantId: envelope.Tenant,
            refundEventId: envelope.EventId,
            originalEventId: originalEventId,
            refundRatio: ratio,
            ct: ct);

        // Failure must not be swallowed — the message should be nacked and land in the DLQ
        if (!result.IsSuccess)
            throw new InvalidOperationException(result.Error ?? "refund_failed");
    }
}
