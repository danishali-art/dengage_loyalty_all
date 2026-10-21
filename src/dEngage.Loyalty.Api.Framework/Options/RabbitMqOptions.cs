namespace dEngage.Loyalty.Api.Framework.Options;

// Loyalty's own messaging settings, section "RabbitMq". The broker connection itself is
// BrokerConnectionSettings (platform "RabbitMQSettings", alignment D8).
public sealed class RabbitMqOptions
{
    // Same allow-list convention as Consumer's ConsumerConfig.GenericEventTypes — an unbound
    // routing key on the direct "loyalty.events" exchange is silently dropped by RabbitMQ.
    public string[] GenericEventTypes { get; set; } = Array.Empty<string>();
}
