using FluentAssertions;
using dEngage.Loyalty.Consumer;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace dEngage.Loyalty.Engine.Tests;

// Platform alignment Phase 1: the Consumer used to fall back to localhost and postgres/guest
// credentials when a value was missing. A deployment with an incomplete mounted config must now
// refuse to start, naming every missing key.
public sealed class ConsumerConfigTests
{
    private static IConfiguration Config(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private static Dictionary<string, string?> Complete() => new()
    {
        ["ConnectionString"] = "Host=db;Database=loyalty",
        ["RedisConnectionString"] = "redis:6379",
        ["RabbitMq:Host"] = "mq",
        ["RabbitMq:User"] = "loyalty",
        ["RabbitMq:Password"] = "pw",
    };

    [Fact]
    public void A_complete_configuration_is_read_as_given()
    {
        var values = Complete();
        values["RabbitMq:Port"] = "5673";
        values["RabbitMq:GenericEventTypes:0"] = "custom.event";

        var config = ConsumerConfig.FromConfiguration(Config(values));

        config.ConnectionString.Should().Be("Host=db;Database=loyalty");
        config.RedisConnectionString.Should().Be("redis:6379");
        config.Broker.Host.Should().Be("mq");
        config.Broker.Port.Should().Be(5673);
        config.Broker.User.Should().Be("loyalty");
        config.Broker.Password.Should().Be("pw");
        config.GenericEventTypes.Should().Equal("custom.event");
    }

    [Fact]
    public void An_empty_configuration_fails_and_names_every_missing_key()
    {
        var act = () => ConsumerConfig.FromConfiguration(Config(new()));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*ConnectionString*RedisConnectionString*RabbitMq:Host*RabbitMq:User*RabbitMq:Password*");
    }

    [Fact]
    public void The_legacy_Redis_key_is_still_accepted()
    {
        var values = Complete();
        values.Remove("RedisConnectionString");
        values["Redis"] = "legacy-redis:6379";

        ConsumerConfig.FromConfiguration(Config(values)).RedisConnectionString.Should().Be("legacy-redis:6379");
    }

    // D12: isolated tenants become queue names (q.loyalty.tenant.<slug>), so only real slugs pass.
    [Fact]
    public void Isolated_tenants_are_read_once_each_and_default_to_none()
    {
        ConsumerConfig.FromConfiguration(Config(Complete())).IsolatedTenants.Should().BeEmpty();

        var values = Complete();
        values["RabbitMq:IsolatedTenants:0"] = "starbucks";
        values["RabbitMq:IsolatedTenants:1"] = "big_tenant_2";
        values["RabbitMq:IsolatedTenants:2"] = "starbucks";

        ConsumerConfig.FromConfiguration(Config(values)).IsolatedTenants.Should().Equal("starbucks", "big_tenant_2");
    }

    [Theory]
    [InlineData("Starbucks")]
    [InlineData("star.bucks")]
    [InlineData("star bucks")]
    public void An_isolated_tenant_that_is_not_a_slug_fails(string slug)
    {
        var values = Complete();
        values["RabbitMq:IsolatedTenants:0"] = slug;

        var act = () => ConsumerConfig.FromConfiguration(Config(values));

        act.Should().Throw<InvalidOperationException>().WithMessage($"*RabbitMq:IsolatedTenants*{slug}*");
    }

    [Fact]
    public void The_port_defaults_to_5672_but_an_invalid_port_fails()
    {
        ConsumerConfig.FromConfiguration(Config(Complete())).Broker.Port.Should().Be(5672);

        var values = Complete();
        values["RabbitMq:Port"] = "not-a-port";
        var act = () => ConsumerConfig.FromConfiguration(Config(values));

        act.Should().Throw<InvalidOperationException>().WithMessage("*RabbitMq:Port*");
    }
}
