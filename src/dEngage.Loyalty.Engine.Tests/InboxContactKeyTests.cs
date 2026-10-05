using System.Text.Json;
using FluentAssertions;
using dEngage.Loyalty.Consumer;
using dEngage.Loyalty.Shared.Events;
using Xunit;

namespace dEngage.Loyalty.Engine.Tests;

// CR 2026-10-02 (Customer 360, D6): the contact_key the Consumer records on the event_inbox row.
public class InboxContactKeyTests
{
    private static EventEnvelope Envelope(string dataJson) => new()
    {
        EventId = Guid.NewGuid().ToString(),
        EventType = EventTypes.OrderCreated,
        Tenant = "t1",
        OccurredAt = DateTime.UtcNow,
        Data = JsonDocument.Parse(dataJson).RootElement.Clone()
    };

    [Fact]
    public void Records_the_events_contact_key() =>
        EventConsumerWorker.InboxContactKey(Envelope("""{"contact_key":"cust_1","amount":"10"}"""))
            .Should().Be("cust_1");

    [Theory]
    [InlineData("""{"order_id":"o-1"}""")]                  // no contact_key (e.g. order.refunded)
    [InlineData("""{"contactKey":"cust_1"}""")]            // camelCase is not the contract
    [InlineData("""{"contact_key":""}""")]
    [InlineData("""{"contact_key":42}""")]
    [InlineData("""[]""")]
    public void Is_null_when_the_event_has_no_usable_contact_key(string dataJson) =>
        EventConsumerWorker.InboxContactKey(Envelope(dataJson)).Should().BeNull();

    // The column is varchar(255): an over-long key is left out instead of failing the inbox
    // insert, so the event is still deduplicated and processed.
    [Fact]
    public void Leaves_out_a_key_longer_than_the_column() =>
        EventConsumerWorker.InboxContactKey(Envelope($$"""{"contact_key":"{{new string('k', 256)}}"}"""))
            .Should().BeNull();
}
