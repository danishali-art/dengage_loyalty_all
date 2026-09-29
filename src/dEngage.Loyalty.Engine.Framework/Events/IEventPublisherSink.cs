namespace dEngage.Loyalty.Engine.Framework.Events;

// Seam an IOutboxTranslator writes through. Mirrors dEngage.Loyalty.Ledger.OutboxService.Enqueue's
// shape exactly (same params, same "adds to the DbContext, does not save" contract) — this
// project cannot reference Ledger (Ledger -> RuleEngine -> Engine.Framework would cycle back),
// so Ledger supplies the concrete adapter at DI composition time in Consumer's Program.cs.
public interface IEventPublisherSink
{
    void Publish(string tenantId, string eventType, string contactKey, object data, string? dedupKey = null);
}
