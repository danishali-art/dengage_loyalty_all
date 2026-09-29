using dEngage.Loyalty.Api.Rules;
using dEngage.Loyalty.Schema;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;
using RuleEntity = dEngage.Loyalty.Schema.Entities.Rule;

namespace dEngage.Loyalty.IntegrationTests.Engine;

// CR-09 (docs/scope-change-rules A10 guarantee #7): RuleVersioningService, standalone against a
// fresh Sqlite-backed LoyaltyDbContext — it only needs the DbContext, not the full
// RulesAppService/IRepository stack.
public sealed class RuleVersioningServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly LoyaltyDbContext _db;
    private readonly RuleVersioningService _sut;

    public RuleVersioningServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<LoyaltyDbContext>().UseSqlite(_connection).Options;
        _db = new LoyaltyDbContext(options);
        _db.Database.EnsureCreated();
        _sut = new RuleVersioningService(_db);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private static RuleEntity NewRule() => new()
    {
        Id = Guid.NewGuid(),
        TenantId = Guid.NewGuid(),
        ProgramId = Guid.NewGuid(),
        Name = "v1 name",
        Type = "FixedBonusRule",
        Trigger = "order.created",
        Calculation = "{}",
        Priority = 100,
        Stackable = false,
        ExclusivityGroup = "g1",
        StackMode = "Additive",
        Status = "active",
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
        // CurrentVersion defaults to 1 (entity default) — a never-edited rule has no
        // RuleVersion rows at all; its one-and-only version lives in this row.
    };

    [Fact]
    public async Task A_never_edited_rule_has_no_archived_versions()
    {
        var entity = NewRule();
        _db.RuleVersions.Count(v => v.RuleId == entity.Id).Should().Be(0);
        entity.CurrentVersion.Should().Be(1);
    }

    [Fact]
    public async Task ArchiveAndBumpAsync_snapshots_the_PRE_edit_state_then_increments_CurrentVersion()
    {
        var entity = NewRule();

        // Simulate RulesAppService.UpdateAsync's call order: archive BEFORE mutating.
        await _sut.ArchiveAndBumpAsync(entity.TenantId, entity, CancellationToken.None);
        entity.Name = "v2 name"; // the edit itself
        await _db.SaveChangesAsync();

        entity.CurrentVersion.Should().Be(2);

        var v1 = _db.RuleVersions.Single(v => v.RuleId == entity.Id && v.VersionNumber == 1);
        v1.Name.Should().Be("v1 name", "the archived snapshot must reflect the PRE-edit state, not the new name");
    }

    [Fact]
    public async Task Three_edits_archive_versions_1_through_3_leaving_the_current_row_at_version_4()
    {
        var entity = NewRule();

        for (var i = 2; i <= 4; i++)
        {
            await _sut.ArchiveAndBumpAsync(entity.TenantId, entity, CancellationToken.None);
            entity.Name = $"v{i} name";
        }
        await _db.SaveChangesAsync();

        entity.CurrentVersion.Should().Be(4);
        entity.Name.Should().Be("v4 name", "the current state lives in the Rule row, not archived");
        _db.RuleVersions.Where(v => v.RuleId == entity.Id).Select(v => v.VersionNumber).OrderBy(x => x)
            .Should().BeEquivalentTo(new[] { 1, 2, 3 }, options => options.WithStrictOrdering());
    }
}
