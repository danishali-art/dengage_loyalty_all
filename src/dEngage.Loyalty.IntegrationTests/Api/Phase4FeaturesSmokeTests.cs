using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using dEngage.Loyalty.Api.Complaints;
using dEngage.Loyalty.Api.ConfigVersions;
using dEngage.Loyalty.Api.Dashboard;
using dEngage.Loyalty.Api.Framework.Auth;
using dEngage.Loyalty.Api.Framework.Json;
using dEngage.Loyalty.Api.Framework.Pagination;
using dEngage.Loyalty.Api.Programs;
using dEngage.Loyalty.IntegrationTests.Fixtures;
using dEngage.Loyalty.Schema.Entities;
using dEngage.Loyalty.Shared;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using AccountTypeEntity = dEngage.Loyalty.Schema.Entities.AccountType;

namespace dEngage.Loyalty.IntegrationTests.Api;

// End-to-end HTTP coverage for the Phase 4 backend features (config version history, complaints,
// program immutability/soft-delete) against the real Nancy pipeline — proves the wiring in
// Program.cs (services + modules) actually works, not just that the app services compile.
public sealed class Phase4FeaturesSmokeTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private const string TenantSlug = "t1";

    public Phase4FeaturesSmokeTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        await _factory.EnsureDbCreatedAsync();

        // The Sqlite in-memory connection is shared across every test in this class (one
        // CustomWebApplicationFactory instance via IClassFixture) — seed the tenant once.
        await using (var db = _factory.CreateDbContext())
        {
            if (!db.Tenants.Any(t => t.Slug == TenantSlug))
            {
                db.Tenants.Add(new Tenant { Id = Guid.NewGuid(), Slug = TenantSlug, Name = TenantSlug, Status = TenantStatus.Active, CreatedAt = DateTime.UtcNow });
                await db.SaveChangesAsync();
            }
        }

        var jwt = _factory.Services.GetRequiredService<IJwtTokenService>();
        var token = JwtIssuingHelper.IssuePlatformAdminToken(jwt);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static StringContent Json(object body) => new(
        System.Text.Json.JsonSerializer.Serialize(body, JsonConventions.Options), System.Text.Encoding.UTF8, "application/json");

    [Fact]
    public async Task Creating_a_program_writes_a_created_config_version()
    {
        var create = new CreateProgramRequest("History Test Program", null);
        var createResponse = await _client.PostAsync($"/api/v1/tenants/{TenantSlug}/programs", Json(create));
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var program = await createResponse.Content.ReadFromJsonAsync<ProgramResponse>(JsonConventions.Options);

        var versionsResponse = await _client.GetAsync(
            $"/api/v1/tenants/{TenantSlug}/config-versions?entityType=Program&entityId={program!.Id}");
        versionsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var versions = await versionsResponse.Content.ReadFromJsonAsync<PagedResult<ConfigVersionSummary>>(JsonConventions.Options);

        versions!.Data.Should().ContainSingle(v => v.ChangeType == "created");
    }

    [Fact]
    public async Task Changing_the_qualifying_account_type_while_the_program_is_active_is_rejected()
    {
        // AccountTypes can only be created once the Program exists, so the qualifying type is
        // assigned while the program is still Inactive — the guard only fires once Active.
        var createResponse = await _client.PostAsync($"/api/v1/tenants/{TenantSlug}/programs", Json(
            new CreateProgramRequest("Lock Test Program", null)));
        var program = await createResponse.Content.ReadFromJsonAsync<ProgramResponse>(JsonConventions.Options);

        Guid firstAccountTypeId, secondAccountTypeId;
        await using (var db = _factory.CreateDbContext())
        {
            var tenantId = db.Tenants.Single(t => t.Slug == TenantSlug).Id;
            firstAccountTypeId = Guid.NewGuid();
            secondAccountTypeId = Guid.NewGuid();
            db.AccountTypes.AddRange(
                new AccountTypeEntity { Id = firstAccountTypeId, TenantId = tenantId, ProgramId = program!.Id, Type = "POINTS", Name = "Points", CreatedAt = DateTime.UtcNow },
                new AccountTypeEntity { Id = secondAccountTypeId, TenantId = tenantId, ProgramId = program.Id, Type = "CASH", Name = "Cash", CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        // 1.3.CL item 1: the qualifying account is now a flag on the account type
        // (PATCH .../account-types/{id} { isTierQualifying }) — same lock, new route.
        var accountTypesUrl = $"/api/v1/tenants/{TenantSlug}/programs/{program!.Id}/account-types";
        var assignWhileInactive = await _client.PatchAsync($"{accountTypesUrl}/{firstAccountTypeId}", Json(new { isTierQualifying = true }));
        assignWhileInactive.StatusCode.Should().Be(HttpStatusCode.OK);

        // 1.3.CL item 8: a program is a draft until published, and only then can be activated.
        (await _client.PostAsync($"/api/v1/tenants/{TenantSlug}/programs/{program.Id}/publish", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var activate = await _client.PatchAsync($"/api/v1/tenants/{TenantSlug}/programs/{program.Id}", Json(
            new UpdateProgramRequest(null, null, ProgramStatus.Active, null, null)));
        activate.StatusCode.Should().Be(HttpStatusCode.OK);

        var changeWhileActive = await _client.PatchAsync($"{accountTypesUrl}/{firstAccountTypeId}", Json(new { isTierQualifying = false }));
        changeWhileActive.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Deleting_an_active_program_is_blocked_but_succeeds_once_inactive()
    {
        var createResponse = await _client.PostAsync($"/api/v1/tenants/{TenantSlug}/programs", Json(
            new CreateProgramRequest("Delete Test Program", null)));
        var program = await createResponse.Content.ReadFromJsonAsync<ProgramResponse>(JsonConventions.Options);

        // 1.3.CL item 8: new programs are inactive drafts — publish (needs an account type) and
        // activate first so the "active program cannot be deleted" guard is actually exercised.
        var programUrl = $"/api/v1/tenants/{TenantSlug}/programs/{program!.Id}";
        (await _client.PostAsync($"{programUrl}/account-types", Json(new { type = "POINTS", name = "Points", config = new { decimals = 0 } })))
            .StatusCode.Should().Be(HttpStatusCode.Created);
        (await _client.PostAsync($"{programUrl}/publish", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await _client.PatchAsync(programUrl, Json(new UpdateProgramRequest(null, null, ProgramStatus.Active))))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var blockedDelete = await _client.DeleteAsync($"/api/v1/tenants/{TenantSlug}/programs/{program!.Id}");
        blockedDelete.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var deactivate = await _client.PatchAsync($"/api/v1/tenants/{TenantSlug}/programs/{program.Id}", Json(
            new UpdateProgramRequest(null, null, ProgramStatus.Inactive, null, null)));
        deactivate.StatusCode.Should().Be(HttpStatusCode.OK);

        var allowedDelete = await _client.DeleteAsync($"/api/v1/tenants/{TenantSlug}/programs/{program.Id}");
        allowedDelete.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var getAfterDelete = await _client.GetAsync($"/api/v1/tenants/{TenantSlug}/programs/{program.Id}");
        getAfterDelete.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Complaint_lifecycle_create_list_and_resolve_updates_the_summary()
    {
        var create = await _client.PostAsync($"/api/v1/tenants/{TenantSlug}/complaints", Json(
            new CreateComplaintRequest(null, "cust-1", "Points not credited", "Customer says points never arrived.")));
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var complaint = await create.Content.ReadFromJsonAsync<ComplaintResponse>(JsonConventions.Options);
        complaint!.Status.Should().Be(ComplaintStatus.Open);

        var summaryBefore = await (await _client.GetAsync($"/api/v1/tenants/{TenantSlug}/complaints/summary"))
            .Content.ReadFromJsonAsync<ComplaintSummaryResponse>(JsonConventions.Options);
        summaryBefore!.Open.Should().BeGreaterThanOrEqualTo(1);

        var resolve = await _client.PatchAsync($"/api/v1/tenants/{TenantSlug}/complaints/{complaint.Id}/status", Json(
            new UpdateComplaintStatusRequest(ComplaintStatus.Resolved)));
        resolve.StatusCode.Should().Be(HttpStatusCode.OK);
        var resolved = await resolve.Content.ReadFromJsonAsync<ComplaintResponse>(JsonConventions.Options);
        resolved!.Status.Should().Be(ComplaintStatus.Resolved);
        resolved.ResolvedAt.Should().NotBeNull();
    }

    // Dashboard/summary is covered separately in E2E/DashboardEndpointE2ETests.cs against a real
    // Postgres — DashboardAppService sums CustomerAccounts.Balance (decimal) via SumAsync, which
    // Sqlite's EF Core provider cannot translate (decimal is TEXT-mapped there), so it 500s under
    // this Sqlite-backed factory even though it works correctly against Postgres.
}
