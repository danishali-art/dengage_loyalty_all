using FluentAssertions;
using dEngage.Loyalty.Consumer;
using dEngage.Loyalty.Shared.Messaging;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace dEngage.Loyalty.Engine.Tests;

// Platform alignment D8 (settings step): the broker connection comes from the platform's
// appsettings.messagebroker.json section "RabbitMQSettings" when it names a host, else from the
// legacy "RabbitMq" keys. Never mixed, never a localhost/guest fallback.
public sealed class BrokerConnectionSettingsTests
{
    private static IConfiguration Config(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private static Dictionary<string, string?> Platform() => new()
    {
        ["RabbitMQSettings:RabbitMQUri"] = "rabbitmq://mq.platform:30010",
        ["RabbitMQSettings:RabbitMQHost"] = "mq.platform",
        ["RabbitMQSettings:RabbitMQPort"] = "30010",
        ["RabbitMQSettings:RabbitMQUsername"] = "dEngage.Loyalty",
        ["RabbitMQSettings:RabbitMQPassword"] = "pw",
        ["RabbitMQSettings:VirtualHost"] = "/loyalty",
        ["RabbitMQSettings:UseSsl"] = "true",
    };

    [Fact]
    public void The_platform_section_is_read_as_the_platform_writes_it()
    {
        var settings = BrokerConnectionSettings.Read(Config(Platform()));

        settings.Problems.Should().BeEmpty();
        settings.Source.Should().Be("RabbitMQSettings");
        settings.Host.Should().Be("mq.platform");
        settings.Port.Should().Be(30010);
        settings.User.Should().Be("dEngage.Loyalty");
        settings.Password.Should().Be("pw");
        settings.VirtualHost.Should().Be("/loyalty");
        settings.UseSsl.Should().BeTrue();
    }

    [Fact]
    public void The_platform_section_wins_and_is_never_mixed_with_the_legacy_keys()
    {
        var values = Platform();
        values.Remove("RabbitMQSettings:RabbitMQPassword");
        values["RabbitMq:Host"] = "mq.legacy";
        values["RabbitMq:User"] = "legacy";
        values["RabbitMq:Password"] = "legacy-pw";

        var settings = BrokerConnectionSettings.Read(Config(values));

        settings.Host.Should().Be("mq.platform");
        settings.Problems.Should().Equal("RabbitMQSettings:RabbitMQPassword");
    }

    [Fact]
    public void Without_a_platform_host_the_legacy_keys_are_used_with_platform_defaults()
    {
        var settings = BrokerConnectionSettings.Read(Config(new()
        {
            ["RabbitMQSettings:RabbitMQHost"] = "",
            ["RabbitMq:Host"] = "mq",
            ["RabbitMq:User"] = "loyalty",
            ["RabbitMq:Password"] = "pw",
        }));

        settings.Problems.Should().BeEmpty();
        settings.Source.Should().Be("RabbitMq");
        settings.Host.Should().Be("mq");
        settings.Port.Should().Be(5672);
        settings.VirtualHost.Should().Be("/");
        settings.UseSsl.Should().BeFalse();
    }

    [Fact]
    public void Nothing_configured_names_both_ways_and_falls_back_to_nothing()
    {
        var settings = BrokerConnectionSettings.Read(Config(new()));

        settings.Problems.Should().Equal("RabbitMQSettings:RabbitMQHost (or legacy RabbitMq:Host)", "RabbitMq:User", "RabbitMq:Password");
        settings.Host.Should().BeEmpty();
        settings.User.Should().BeEmpty();
    }

    [Theory]
    [InlineData("RabbitMQSettings:RabbitMQPort", "0")]
    [InlineData("RabbitMQSettings:RabbitMQPort", "port")]
    [InlineData("RabbitMQSettings:UseSsl", "yes")]
    public void Invalid_platform_values_are_reported(string key, string value)
    {
        var values = Platform();
        values[key] = value;

        BrokerConnectionSettings.Read(Config(values)).Problems.Should().ContainSingle().Which.Should().StartWith(key);
    }

    [Fact]
    public void The_consumer_uses_the_platform_section_and_fails_on_an_incomplete_one()
    {
        var values = Platform();
        values["ConnectionString"] = "Host=db;Database=loyalty";
        values["RedisConnectionString"] = "redis:6379";

        var factory = ConsumerConfig.FromConfiguration(Config(values)).CreateConnectionFactory(dispatchConsumersAsync: true);
        factory.HostName.Should().Be("mq.platform");
        factory.Port.Should().Be(30010);
        factory.VirtualHost.Should().Be("/loyalty");
        factory.Ssl.Enabled.Should().BeTrue();
        factory.Ssl.ServerName.Should().Be("mq.platform");
        factory.DispatchConsumersAsync.Should().BeTrue();

        values.Remove("RabbitMQSettings:RabbitMQUsername");
        var act = () => ConsumerConfig.FromConfiguration(Config(values));
        act.Should().Throw<InvalidOperationException>().WithMessage("*RabbitMQSettings:RabbitMQUsername*");
    }
}
