namespace dEngage.Loyalty.Consumer;

public class ConsumerConfig
{
    public string ConnectionString { get; set; } = default!;
    public string RedisConnectionString { get; set; } = default!;
    public string RabbitMqHost { get; set; } = default!;
    public int RabbitMqPort { get; set; } = 5672;
    public string RabbitMqUser { get; set; } = default!;
    public string RabbitMqPassword { get; set; } = default!;

    // Non-built-in event types to bind on q.loyalty. Direct exchange: an unbound
    // routing key is dropped silently — new generic types require config + restart.
    public string[] GenericEventTypes { get; set; } = Array.Empty<string>();

    // Fail fast at startup. The Consumer used to fall back to localhost and postgres/guest
    // credentials when a value was missing, so a deployment with an incomplete mounted config
    // started "fine" against the wrong database (platform alignment, Phase 1). Key names match
    // the Api's ApiOptions/RabbitMqOptions.
    public static ConsumerConfig FromConfiguration(IConfiguration configuration)
    {
        var missing = new List<string>();

        string Required(string key, string? legacyKey = null)
        {
            var value = configuration[key];
            if (string.IsNullOrWhiteSpace(value) && legacyKey is not null)
                value = configuration[legacyKey];
            if (string.IsNullOrWhiteSpace(value))
                missing.Add(legacyKey is null ? key : $"{key} (or legacy {legacyKey})");
            return value ?? string.Empty;
        }

        var config = new ConsumerConfig
        {
            ConnectionString = Required("ConnectionString"),
            // "Redis" was the Consumer's own key before it was aligned with the Api.
            RedisConnectionString = Required("RedisConnectionString", legacyKey: "Redis"),
            RabbitMqHost = Required("RabbitMq:Host"),
            RabbitMqUser = Required("RabbitMq:User"),
            RabbitMqPassword = Required("RabbitMq:Password"),
            GenericEventTypes = configuration.GetSection("RabbitMq:GenericEventTypes").Get<string[]>() ?? Array.Empty<string>()
        };

        var portValue = configuration["RabbitMq:Port"];
        if (!string.IsNullOrWhiteSpace(portValue))
        {
            if (!int.TryParse(portValue, out var port) || port <= 0)
                missing.Add("RabbitMq:Port (must be a positive number)");
            else
                config.RabbitMqPort = port;
        }

        if (missing.Count > 0)
            throw new InvalidOperationException(
                "Consumer configuration is incomplete. Set: " + string.Join(", ", missing) + ".");

        return config;
    }
}
