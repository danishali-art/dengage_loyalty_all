using System.Text;
using dEngage.Loyalty.Consumer;
using dEngage.Loyalty.Shared;
using FluentAssertions;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Testcontainers.RabbitMq;
using Xunit;
using dEngage.Loyalty.Contract;

namespace dEngage.Loyalty.IntegrationTests.E2E;

// Platform alignment D12: an isolated tenant's events are moved from q.loyalty to
// q.loyalty.tenant.<slug>. Moving must keep the tenant's order and the message itself intact,
// and a lane must dead-letter exactly like q.loyalty. Needs Docker (real RabbitMQ).
public sealed class TenantLanesE2ETests : IAsyncLifetime
{
    private readonly RabbitMqContainer _rabbit = new RabbitMqBuilder().Build();
    private IConnection _connection = default!;

    public async Task InitializeAsync()
    {
        await _rabbit.StartAsync();
        _connection = new ConnectionFactory { Uri = new Uri(_rabbit.GetConnectionString()) }.CreateConnection();
    }

    public async Task DisposeAsync()
    {
        _connection.Dispose();
        await _rabbit.DisposeAsync();
    }

    [Fact]
    public void Forwarded_events_keep_their_order_body_and_headers()
    {
        using var channel = _connection.CreateModel();
        channel.ExchangeDeclare(MessagingTopology.DeadLetterExchange, ExchangeType.Fanout, durable: true);
        var lane = TenantLanes.Declare(channel, "starbucks");

        using var confirmChannel = _connection.CreateModel();
        confirmChannel.ConfirmSelect();

        for (var i = 0; i < 25; i++)
            TenantLanes.Forward(confirmChannel, Delivery(channel, $"{{\"event_id\":\"e{i}\"}}", i), "starbucks");

        for (var i = 0; i < 25; i++)
        {
            var got = channel.BasicGet(lane, autoAck: true);
            got.Should().NotBeNull($"message {i} should have reached {lane}");
            Encoding.UTF8.GetString(got!.Body.ToArray()).Should().Be($"{{\"event_id\":\"e{i}\"}}");
            got.BasicProperties.Persistent.Should().BeTrue();
            got.BasicProperties.ContentType.Should().Be("application/json");
            got.BasicProperties.MessageId.Should().Be($"msg-{i}");
            Encoding.UTF8.GetString((byte[])got.BasicProperties.Headers["x-sequence"]).Should().Be(i.ToString());
        }
        channel.BasicGet(lane, autoAck: true).Should().BeNull();
    }

    [Fact]
    public void A_failed_event_in_a_lane_dead_letters_into_the_shared_dlq()
    {
        using var channel = _connection.CreateModel();
        // Same DLX/DLQ wiring as EventConsumerWorker.
        channel.ExchangeDeclare(MessagingTopology.DeadLetterExchange, ExchangeType.Fanout, durable: true);
        channel.QueueDeclare(MessagingTopology.DeadLetterQueue, durable: true, exclusive: false, autoDelete: false);
        channel.QueueBind(MessagingTopology.DeadLetterQueue, MessagingTopology.DeadLetterExchange, routingKey: "");
        var lane = TenantLanes.Declare(channel, "fintech");

        using var confirmChannel = _connection.CreateModel();
        confirmChannel.ConfirmSelect();
        TenantLanes.Forward(confirmChannel, Delivery(channel, "{\"event_id\":\"bad\"}", 1), "fintech");

        var got = channel.BasicGet(lane, autoAck: false);
        channel.BasicNack(got!.DeliveryTag, multiple: false, requeue: false); // what the worker does on failure

        var deadLettered = WaitForMessage(channel, MessagingTopology.DeadLetterQueue);
        Encoding.UTF8.GetString(deadLettered.Body.ToArray()).Should().Be("{\"event_id\":\"bad\"}");
    }

    [Fact]
    public void Declaring_a_lane_twice_is_harmless()
    {
        using var channel = _connection.CreateModel();
        TenantLanes.Declare(channel, "starbucks");

        var act = () => TenantLanes.Declare(channel, "starbucks");

        act.Should().NotThrow("every Consumer start re-declares its lanes");
    }

    private static BasicDeliverEventArgs Delivery(IModel channel, string body, int sequence)
    {
        var properties = channel.CreateBasicProperties();
        properties.ContentType = "application/json";
        properties.MessageId = $"msg-{sequence}";
        properties.Headers = new Dictionary<string, object> { ["x-sequence"] = Encoding.UTF8.GetBytes(sequence.ToString()) };
        return new BasicDeliverEventArgs("test", (ulong)sequence + 1, false, MessagingTopology.Exchange,
            "order.created", properties, Encoding.UTF8.GetBytes(body));
    }

    private static BasicGetResult WaitForMessage(IModel channel, string queue)
    {
        // Dead-lettering is asynchronous in the broker.
        for (var attempt = 0; attempt < 50; attempt++)
        {
            var result = channel.BasicGet(queue, autoAck: true);
            if (result is not null) return result;
            Thread.Sleep(100);
        }
        throw new TimeoutException($"No message arrived in {queue}.");
    }
}
