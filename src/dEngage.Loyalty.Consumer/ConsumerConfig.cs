namespace dEngage.Loyalty.Consumer;

public class ConsumerConfig
{
    public string ConnectionString { get; set; } = default!;
    public string RedisConnectionString { get; set; } = default!;
    public string RabbitMqHost { get; set; } = "localhost";
    public int RabbitMqPort { get; set; } = 5672;
    public string RabbitMqUser { get; set; } = "guest";
    public string RabbitMqPassword { get; set; } = "guest";

    // Non-built-in event types to bind on q.loyalty. Direct exchange: an unbound
    // routing key is dropped silently — new generic types require config + restart.
    public string[] GenericEventTypes { get; set; } = Array.Empty<string>();
}
