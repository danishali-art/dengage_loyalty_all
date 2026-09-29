using System.Text;
using System.Text.Json;
using dEngage.Loyalty.Api.Framework.Json;
using dEngage.Loyalty.Shared.Events;
using RabbitMQ.Client;

namespace dEngage.Loyalty.Api.Framework.Messaging;

// Adapter over RabbitMQ.Client — the event-ingestion gateway's only way to reach "loyalty.events".
// The connection is a long-lived singleton (see RabbitMqConnectionHolder); channels are created
// per publish since IModel is not thread-safe and this type serves many concurrent HTTP requests,
// unlike Consumer's single sequential background loop.
public sealed class RabbitMqEventPublisher(IRabbitMqConnectionHolder connectionHolder) : IEventPublisher
{
    private const string Exchange = "loyalty.events";

    public Task PublishAsync(EventEnvelope envelope, CancellationToken ct)
    {
        using var channel = connectionHolder.Connection.CreateModel();
        channel.ExchangeDeclare(Exchange, ExchangeType.Direct, durable: true);

        var props = channel.CreateBasicProperties();
        props.Persistent = true;
        props.ContentType = "application/json";
        props.MessageId = envelope.EventId;

        var body = JsonSerializer.SerializeToUtf8Bytes(envelope, JsonConventions.Options);

        channel.BasicPublish(
            exchange: Exchange,
            routingKey: envelope.EventType,
            basicProperties: props,
            body: body);

        return Task.CompletedTask;
    }
}

public interface IRabbitMqConnectionHolder : IDisposable
{
    IConnection Connection { get; }
}

public sealed class RabbitMqConnectionHolder : IRabbitMqConnectionHolder
{
    public IConnection Connection { get; }

    public RabbitMqConnectionHolder(Options.RabbitMqOptions options)
    {
        var factory = new ConnectionFactory
        {
            HostName = options.Host,
            Port = options.Port,
            UserName = options.User,
            Password = options.Password
        };
        Connection = factory.CreateConnection("loyalty-api-gateway");
    }

    public void Dispose() => Connection.Dispose();
}
