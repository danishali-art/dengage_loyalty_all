using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using dEngage.Loyalty.Api.ConfigVersions;
using dEngage.Loyalty.Api.Framework.Auth;
using dEngage.Loyalty.Api.Framework.Json;
using dEngage.Loyalty.Api.Framework.Pagination;
using dEngage.Loyalty.Api.Programs;
using dEngage.Loyalty.Api.Rules;
using dEngage.Loyalty.Api.Tiers;
using dEngage.Loyalty.IntegrationTests.Fixtures;
using dEngage.Loyalty.RuleEngine.Models;
using dEngage.Loyalty.Schema.Entities;
using dEngage.Loyalty.Shared;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace dEngage.Loyalty.IntegrationTests.Api;

// 1.3.CL items 7–9 (docs/scope-changes/2026-09-28-program-account-type-changes.md §3.8–3.10):
// Draft → Publish lifecycle, the Active toggle gate, the unpublished-changes flag across nested
// resources, and the aggregate ProgramPublication ConfigVersion snapshot.
public sealed class ProgramPublishCl13ApiTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private const string TenantSlug = "cl13-publish";

    public ProgramPublishCl13ApiTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        await _factory.EnsureDbCreatedAsync();
        await using (var db = _factory.CreateDbContext())
        {
            if (!db.Tenants.Any(t => t.Slug == TenantSlug))
            {
                db.Tenants.Add(new Tenant { Id = Guid.NewGuid(), Slug = TenantSlug, Name = TenantSlug, Status = TenantStatus.Active, CreatedAt = DateTime.UtcNow });
                await db.SaveChangesAsync();
            }
        }
        var jwt = _factory.Services.GetRequiredService<IJwtTokenService>();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", JwtIssuingHelper.IssuePlatformAdminToken(jwt));
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static StringContent Json(object body) => new(
        JsonSerializer.Serialize(body, JsonConventions.Options), System.Text.Encoding.UTF8, "application/json");

    private const string ProgramsUrl = $"/api/v1/tenants/{TenantSlug}/programs";

    private async Task<ProgramResponse> CreateProgramAsync()
    {
        var response = await _client.PostAsync(ProgramsUrl, Json(new CreateProgramRequest($"pub-{Guid.NewGuid():N}", null)));
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<ProgramResponse>(JsonConventions.Options))!;
    }

    private async Task<ProgramResponse> GetProgramAsync(Guid id) =>
        (await (await _client.GetAsync($"{ProgramsUrl}/{id}")).Content.ReadFromJsonAsync<ProgramResponse>(JsonConventions.Options))!;

    private async Task<Guid> AddPointsAccountTypeAsync(Guid programId, bool isTierQualifying = false)
    {
        var response = await _client.PostAsync($"{ProgramsUrl}/{programId}/account-types",
            Json(new { type = "POINTS", name = $"pts-{Guid.NewGuid():N}"[..12], config = new { decimals = 0 }, isTierQualifying }));
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(JsonConventions.Options)).GetProperty("id").GetGuid();
    }

    private Task<HttpResponseMessage> PublishAsync(Guid programId) => _client.PostAsync($"{ProgramsUrl}/{programId}/publish", null);

    private async Task<ProgramResponse> PublishOkAsync(Guid programId)
    {
        var response = await PublishAsync(programId);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<ProgramResponse>(JsonConventions.Options))!;
    }

    // ── Item 8: a new program is a draft ────────────────────────────────────────────────
    [Fact]
    public async Task A_new_program_starts_as_an_inactive_draft()
    {
        var program = await CreateProgramAsync();

        program.PublicationStatus.Should().Be(ProgramPublicationStatus.Draft);
        program.Status.Should().Be(ProgramStatus.Inactive);
        program.PublishedVersion.Should().BeNull();
        program.HasUnpublishedChanges.Should().BeFalse();
    }

    [Theory]
    [InlineData(ProgramStatus.Active)]
    [InlineData(ProgramStatus.Inactive)]
    public async Task Status_cannot_be_chosen_on_create(string status)
    {
        (await _client.PostAsync(ProgramsUrl, Json(new CreateProgramRequest("with-status", null, status))))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── Items 7/8: the Active toggle only works once published ──────────────────────────
    [Fact]
    public async Task A_draft_cannot_be_activated_but_a_published_program_can_be_toggled()
    {
        var program = await CreateProgramAsync();
        await AddPointsAccountTypeAsync(program.Id);

        var activateDraft = await _client.PatchAsync($"{ProgramsUrl}/{program.Id}", Json(new UpdateProgramRequest(null, null, ProgramStatus.Active)));
        activateDraft.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await activateDraft.Content.ReadAsStringAsync()).Should().Contain("program_not_published");

        await PublishOkAsync(program.Id);

        var activate = await _client.PatchAsync($"{ProgramsUrl}/{program.Id}", Json(new UpdateProgramRequest(null, null, ProgramStatus.Active)));
        activate.StatusCode.Should().Be(HttpStatusCode.OK);
        var toggled = (await activate.Content.ReadFromJsonAsync<ProgramResponse>(JsonConventions.Options))!;
        toggled.Status.Should().Be(ProgramStatus.Active);
        toggled.HasUnpublishedChanges.Should().BeFalse("toggling Active/Inactive is not a configuration change");
    }

    // ── §5 c: publish prerequisites ──────────────────────────────────────────────────────
    [Fact]
    public async Task Publishing_needs_an_account_type_and_a_tier_qualifying_wallet_when_tiers_exist()
    {
        var program = await CreateProgramAsync();

        var noAccountTypes = await PublishAsync(program.Id);
        noAccountTypes.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await noAccountTypes.Content.ReadAsStringAsync()).Should().Contain("publish_prerequisites_not_met");

        await AddPointsAccountTypeAsync(program.Id);
        (await _client.PostAsync($"{ProgramsUrl}/{program.Id}/tiers",
                Json(new CreateTierRequest("gold", "Gold", 1000m, "lifetime", null, 0, 1))))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        (await PublishAsync(program.Id)).StatusCode.Should().Be(HttpStatusCode.Conflict, "tiers need a tier qualifying account type");

        await AddPointsAccountTypeAsync(program.Id, isTierQualifying: true);
        await PublishOkAsync(program.Id);
    }

    // ── Item 9: one aggregate snapshot per publish ──────────────────────────────────────
    [Fact]
    public async Task Publish_writes_one_aggregate_snapshot_and_only_again_after_a_change()
    {
        var program = await CreateProgramAsync();
        var pointsId = await AddPointsAccountTypeAsync(program.Id);
        var rule = await (await _client.PostAsync($"{ProgramsUrl}/{program.Id}/rules", Json(new CreateRuleRequest(
                "earn", "remittance", pointsId, RuleTypes.SpendRule, new RuleCalculation { Factor = 1m }, null, null,
                10, false, null, null, null, null, null))))
            .Content.ReadFromJsonAsync<RuleResponse>(JsonConventions.Options);

        var published = await PublishOkAsync(program.Id);
        published.PublicationStatus.Should().Be(ProgramPublicationStatus.Published);
        published.PublishedVersion.Should().Be(1);
        published.PublishedAt.Should().NotBeNull();
        published.PublishedBy.Should().NotBeNullOrEmpty();
        published.HasUnpublishedChanges.Should().BeFalse();

        (await PublishAsync(program.Id)).StatusCode.Should().Be(HttpStatusCode.Conflict, "nothing changed since the last publish");

        var versions = await (await _client.GetAsync(
                $"/api/v1/tenants/{TenantSlug}/config-versions?entityType={ProgramsAppService.PublicationEntityType}&entityId={program.Id}"))
            .Content.ReadFromJsonAsync<PagedResult<ConfigVersionSummary>>(JsonConventions.Options);
        var only = versions!.Data.Should().ContainSingle().Subject;
        only.ChangeType.Should().Be(ConfigChangeType.Published);

        var detail = await (await _client.GetAsync($"/api/v1/tenants/{TenantSlug}/config-versions/{only.Id}"))
            .Content.ReadFromJsonAsync<ConfigVersionDetail>(JsonConventions.Options);
        var snapshot = JsonDocument.Parse(detail!.Snapshot).RootElement;
        snapshot.GetProperty("AccountTypes").GetArrayLength().Should().Be(1);
        var publishedRule = snapshot.GetProperty("Rules")[0];
        publishedRule.GetProperty("RuleId").GetGuid().Should().Be(rule!.Id);
        publishedRule.GetProperty("Version").GetInt32().Should().Be(rule.Version, "the snapshot pins the rule version postings reference");
        snapshot.TryGetProperty("Program", out _).Should().BeTrue();

        // §5 f (keep both): the per-edit Program audit rows are still written.
        var perEdit = await (await _client.GetAsync($"/api/v1/tenants/{TenantSlug}/config-versions?entityType=Program&entityId={program.Id}"))
            .Content.ReadFromJsonAsync<PagedResult<ConfigVersionSummary>>(JsonConventions.Options);
        perEdit!.Data.Should().Contain(v => v.ChangeType == ConfigChangeType.Created);

        // A change after publishing → unpublished changes → the next publish is version 2.
        (await _client.PatchAsync($"{ProgramsUrl}/{program.Id}", Json(new UpdateProgramRequest("renamed", null, null))))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await GetProgramAsync(program.Id)).HasUnpublishedChanges.Should().BeTrue();
        (await PublishOkAsync(program.Id)).PublishedVersion.Should().Be(2);
    }

    // ── Item 8: nested changes mark the program as having unpublished changes ────────────
    public static TheoryData<string> NestedChanges => new() { "account-type", "tier", "rule", "rule-status" };

    [Theory]
    [MemberData(nameof(NestedChanges))]
    public async Task Changing_nested_config_after_publish_marks_unpublished_changes(string change)
    {
        var program = await CreateProgramAsync();
        var pointsId = await AddPointsAccountTypeAsync(program.Id, isTierQualifying: true);
        var rule = await (await _client.PostAsync($"{ProgramsUrl}/{program.Id}/rules", Json(new CreateRuleRequest(
                "earn", "remittance", pointsId, RuleTypes.SpendRule, new RuleCalculation { Factor = 1m }, null, null,
                10, false, null, null, null, null, null))))
            .Content.ReadFromJsonAsync<RuleResponse>(JsonConventions.Options);
        await PublishOkAsync(program.Id);
        (await GetProgramAsync(program.Id)).HasUnpublishedChanges.Should().BeFalse();

        var response = change switch
        {
            "account-type" => await _client.PatchAsync($"{ProgramsUrl}/{program.Id}/account-types/{pointsId}", Json(new { name = "Renamed" })),
            "tier" => await _client.PostAsync($"{ProgramsUrl}/{program.Id}/tiers", Json(new CreateTierRequest("silver", "Silver", 0m, "lifetime", null, 0, 1))),
            "rule" => await _client.PatchAsync($"{ProgramsUrl}/{program.Id}/rules/{rule!.Id}", Json(new { priority = 99 })),
            "rule-status" => await _client.PatchAsync($"{ProgramsUrl}/{program.Id}/rules/{rule!.Id}/status", Json(new SetRuleStatusRequest(RuleStatus.Disabled))),
            _ => throw new ArgumentOutOfRangeException(nameof(change))
        };
        response.IsSuccessStatusCode.Should().BeTrue(await response.Content.ReadAsStringAsync());

        (await GetProgramAsync(program.Id)).HasUnpublishedChanges.Should().BeTrue();
    }

    [Fact]
    public async Task A_draft_program_does_not_accumulate_unpublished_changes()
    {
        var program = await CreateProgramAsync();
        await AddPointsAccountTypeAsync(program.Id);
        (await GetProgramAsync(program.Id)).HasUnpublishedChanges.Should().BeFalse("everything in a draft is unpublished by definition");
    }
}
