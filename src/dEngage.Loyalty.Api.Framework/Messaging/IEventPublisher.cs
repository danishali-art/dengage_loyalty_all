using dEngage.Loyalty.Contract;

namespace dEngage.Loyalty.Api.Framework.Messaging;

public interface IEventPublisher
{
    Task PublishAsync(EventEnvelope envelope, CancellationToken ct);
}
