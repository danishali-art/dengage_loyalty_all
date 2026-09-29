using System.Text.Json;
using dEngage.Loyalty.Consumer;
using dEngage.Loyalty.IntegrationTests.Fixtures;
using dEngage.Loyalty.RuleEngine;
using dEngage.Loyalty.RuleEngine.Models;
using dEngage.Loyalty.Schema;
using dEngage.Loyalty.Shared;
using dEngage.Loyalty.Shared.Events;
using FluentAssertions;
using Moq;
using Xunit;
using ProgramEntity = dEngage.Loyalty.Schema.Entities.Program;

namespace dEngage.Loyalty.IntegrationTests.Engine;

// 1.3.CL item 8: the consumer only hands events to programs that are published AND active — a
// draft (even one forced active in the DB) and a published-but-inactive program never run.
public sealed class ProgramPublicationGateCl13Tests : IDisposable
{
    private readonly RuleEngineTestHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    private Guid AddProgram(string status, string publicationStatus)
    {
        var id = Guid.NewGuid();
        _harness.Db.Programs.Add(new ProgramEntity
        {
            Id = id, TenantId = _harness.TenantGuid, Name = $"{status}-{publicationStatus}",
            Status = status, PublicationStatus = publicationStatus, CreatedAt = DateTime.UtcNow
        });
        _harness.Db.SaveChanges();
        return id;
    }

    [Fact]
    public async Task Only_published_and_active_programs_are_evaluated()
    {
        var live = AddProgram(ProgramStatus.Active, ProgramPublicationStatus.Published);
        var draft = AddProgram(ProgramStatus.Active, ProgramPublicationStatus.Draft);
        var paused = AddProgram(ProgramStatus.Inactive, ProgramPublicationStatus.Published);

        var engine = new Mock<IRuleEngine>();
        var service = new CampaignEvaluationService(engine.Object, _harness.Db,
            new TenantSlugResolver(_harness.Db, new TenantSlugCache()));

        await service.EvaluateAsync(new EventEnvelope
        {
            EventId = "evt-gate-1",
            EventType = "order.created",
            Tenant = RuleEngineTestHarness.TenantSlug,
            OccurredAt = DateTime.UtcNow,
            Data = JsonSerializer.SerializeToElement(new { contact_key = "gate_c", amount = "10.00" })
        }, CancellationToken.None);

        engine.Verify(e => e.ProcessEventAsync(It.IsAny<string>(), live, It.IsAny<string>(), It.IsAny<EvaluationEvent>(), It.IsAny<CancellationToken>()), Times.Once);
        engine.Verify(e => e.ProcessEventAsync(It.IsAny<string>(), draft, It.IsAny<string>(), It.IsAny<EvaluationEvent>(), It.IsAny<CancellationToken>()), Times.Never);
        engine.Verify(e => e.ProcessEventAsync(It.IsAny<string>(), paused, It.IsAny<string>(), It.IsAny<EvaluationEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
