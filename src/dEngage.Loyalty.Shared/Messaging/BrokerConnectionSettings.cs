using Microsoft.Extensions.Configuration;

namespace dEngage.Loyalty.Shared.Messaging;

// The RabbitMQ connection for the Api, Public.Api and the Consumer (platform alignment D8, settings
// step). dEngage services get theirs from appsettings.messagebroker.json, section "RabbitMQSettings",
// which the platform host loads from CONFIG_PATH; loyalty reads the same section first, so ops
// configure the broker the same way for every service. When that section has no host, the older
// "RabbitMq" keys are used (kept for one release so existing deployments keep working).
//
// Only the connection comes from here. Loyalty's own messaging settings (GenericEventTypes,
// IsolatedTenants) stay under "RabbitMq", and loyalty keeps its own RabbitMQ code and wire contract:
// the platform's MassTransit-based broker library is not used (see docs/platform-alignment/phase-3.md).
public sealed class BrokerConnectionSettings
{
    public const string PlatformSection = "RabbitMQSettings";
    public const string LegacySection = "RabbitMq";

    public string Host { get; set; } = "";
    public int Port { get; set; } = 5672;
    public string User { get; set; } = "";
    public string Password { get; set; } = "";
    public string VirtualHost { get; set; } = "/";
    public bool UseSsl { get; set; }

    // The section the values were read from, for startup errors and logs.
    public string Source { get; set; } = "";

    // What is missing or invalid, by configuration key. Empty when the settings are usable; the
    // hosts refuse to start otherwise (no localhost or guest/guest fallback).
    public List<string> Problems { get; } = [];

    public static BrokerConnectionSettings Read(IConfiguration configuration)
    {
        var settings = new BrokerConnectionSettings();
        settings.LoadFrom(configuration);
        return settings;
    }

    public void LoadFrom(IConfiguration configuration)
    {
        Problems.Clear();
        var platform = configuration.GetSection(PlatformSection);
        if (!string.IsNullOrWhiteSpace(platform["RabbitMQHost"]))
        {
            Source = PlatformSection;
            Host = Required(platform, "RabbitMQHost");
            User = Required(platform, "RabbitMQUsername");
            Password = Required(platform, "RabbitMQPassword");
            Port = OptionalPort(platform, "RabbitMQPort");
            VirtualHost = string.IsNullOrWhiteSpace(platform["VirtualHost"]) ? "/" : platform["VirtualHost"]!;
            var useSsl = platform["UseSsl"];
            if (string.IsNullOrWhiteSpace(useSsl))
                UseSsl = false;
            else if (bool.TryParse(useSsl, out var ssl))
                UseSsl = ssl;
            else
                Problems.Add($"{PlatformSection}:UseSsl (must be true or false)");
            return;
        }

        var legacy = configuration.GetSection(LegacySection);
        Source = LegacySection;
        if (string.IsNullOrWhiteSpace(legacy["Host"]))
            Problems.Add($"{PlatformSection}:RabbitMQHost (or legacy {LegacySection}:Host)");
        Host = legacy["Host"] ?? "";
        User = Required(legacy, "User");
        Password = Required(legacy, "Password");
        Port = OptionalPort(legacy, "Port");
        VirtualHost = string.IsNullOrWhiteSpace(legacy["VirtualHost"]) ? "/" : legacy["VirtualHost"]!;
        UseSsl = false;
    }

    private string Required(IConfigurationSection section, string key)
    {
        var value = section[key];
        if (string.IsNullOrWhiteSpace(value))
            Problems.Add($"{section.Path}:{key}");
        return value ?? "";
    }

    private int OptionalPort(IConfigurationSection section, string key)
    {
        var value = section[key];
        if (string.IsNullOrWhiteSpace(value))
            return 5672;
        if (int.TryParse(value, out var port) && port > 0)
            return port;
        Problems.Add($"{section.Path}:{key} (must be a positive number)");
        return 5672;
    }
}
