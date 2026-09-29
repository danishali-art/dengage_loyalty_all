namespace dEngage.Loyalty.Api.Framework.Options;

public sealed class RabbitMqOptions
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 5672;
    public string User { get; set; } = "guest";
    public string Password { get; set; } = "guest";

    // Same allow-list convention as Consumer's ConsumerConfig.GenericEventTypes — an unbound
    // routing key on the direct "loyalty.events" exchange is silently dropped by RabbitMQ.
    public string[] GenericEventTypes { get; set; } = Array.Empty<string>();
}
