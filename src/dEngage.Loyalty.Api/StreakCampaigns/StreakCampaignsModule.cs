using FluentValidation;
using dEngage.Loyalty.Api.Framework.Json;
using dEngage.Loyalty.Api.Framework.Modules;
using dEngage.Loyalty.Api.Framework.Validation;
using HttpStatusCode = System.Net.HttpStatusCode;

namespace dEngage.Loyalty.Api.StreakCampaigns;

// /api/v1/tenants/{tenantId}/programs/{programId}/streak-campaigns
public sealed class StreakCampaignsModule : TenantScopedModule
{
    public StreakCampaignsModule(
        IStreakCampaignsAppService appService,
        IValidator<CreateStreakCampaignRequest> createValidator,
        IValidator<UpdateStreakCampaignRequest> updateValidator,
        IValidator<SetStreakCampaignStatusRequest> statusValidator)
        : base("/api/v1/tenants/{tenantId}/programs/{programId}/streak-campaigns")
    {
        MapGet("", async (parameters, ct) =>
        {
            var tenantId = RequireTenantScope(RouteParam(parameters, "tenantId"));
            var programId = RouteGuidParam(parameters, "programId");

            var filter = new StreakCampaignListFilter(
                (string?)Request.Query["event"],
                (string?)Request.Query["status"]);

            return JsonResponses.Ok(await appService.ListAsync(tenantId, programId, filter, ReadPageRequest(), ct));
        });

        MapGet("/{campaignId}", async (parameters, ct) =>
        {
            var tenantId = RequireTenantScope(RouteParam(parameters, "tenantId"));
            var programId = RouteGuidParam(parameters, "programId");
            var campaignId = RouteGuidParam(parameters, "campaignId");
            return JsonResponses.Ok(await appService.GetAsync(tenantId, programId, campaignId, ct));
        });

        MapPost("", async (parameters, ct) =>
        {
            var tenantId = RequireTenantScope(RouteParam(parameters, "tenantId"));
            var programId = RouteGuidParam(parameters, "programId");
            var request = await Request.ReadValidatedJsonBodyAsync(createValidator, ct);
            return JsonResponses.Ok(await appService.CreateAsync(tenantId, programId, request, ct), HttpStatusCode.Created);
        });

        MapPatch("/{campaignId}", async (parameters, ct) =>
        {
            var tenantId = RequireTenantScope(RouteParam(parameters, "tenantId"));
            var programId = RouteGuidParam(parameters, "programId");
            var campaignId = RouteGuidParam(parameters, "campaignId");
            var request = await Request.ReadValidatedJsonBodyAsync(updateValidator, ct);
            return JsonResponses.Ok(await appService.UpdateAsync(tenantId, programId, campaignId, request, ct));
        });

        MapPatch("/{campaignId}/status", async (parameters, ct) =>
        {
            var tenantId = RequireTenantScope(RouteParam(parameters, "tenantId"));
            var programId = RouteGuidParam(parameters, "programId");
            var campaignId = RouteGuidParam(parameters, "campaignId");
            var request = await Request.ReadValidatedJsonBodyAsync(statusValidator, ct);
            return JsonResponses.Ok(await appService.SetStatusAsync(tenantId, programId, campaignId, request.Status, ct));
        });

        MapDelete("/{campaignId}", async (parameters, ct) =>
        {
            var tenantId = RequireTenantScope(RouteParam(parameters, "tenantId"));
            var programId = RouteGuidParam(parameters, "programId");
            var campaignId = RouteGuidParam(parameters, "campaignId");
            await appService.DeleteAsync(tenantId, programId, campaignId, ct);
            return JsonResponses.NoContent();
        });
    }
}
