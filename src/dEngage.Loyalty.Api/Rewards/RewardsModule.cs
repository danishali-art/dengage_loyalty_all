using FluentValidation;
using dEngage.Loyalty.Api.Framework.Json;
using dEngage.Loyalty.Api.Framework.Modules;
using dEngage.Loyalty.Api.Framework.Validation;
using HttpStatusCode = System.Net.HttpStatusCode;

namespace dEngage.Loyalty.Api.Rewards;

// /api/v1/tenants/{tenantId}/programs/{programId}/rewards
public sealed class RewardsModule : TenantScopedModule
{
    public RewardsModule(
        IRewardsAppService appService,
        IValidator<CreateRewardRequest> createValidator,
        IValidator<UpdateRewardRequest> updateValidator)
        : base("/api/v1/tenants/{tenantId}/programs/{programId}/rewards")
    {
        MapGet("", async (parameters, ct) =>
        {
            var tenantId = RequireTenantScope(RouteParam(parameters, "tenantId"));
            var programId = RouteGuidParam(parameters, "programId");
            return JsonResponses.Ok(await appService.ListAsync(tenantId, programId, ReadPageRequest(), ct));
        });

        MapPost("", async (parameters, ct) =>
        {
            var tenantId = RequireTenantScope(RouteParam(parameters, "tenantId"));
            var programId = RouteGuidParam(parameters, "programId");
            var request = await Request.ReadValidatedJsonBodyAsync(createValidator, ct);
            var createdBy = RequireAuthenticated().SubjectId;
            return JsonResponses.Ok(await appService.CreateAsync(tenantId, programId, request, createdBy, ct), HttpStatusCode.Created);
        });

        MapPatch("/{rewardId}", async (parameters, ct) =>
        {
            var tenantId = RequireTenantScope(RouteParam(parameters, "tenantId"));
            var programId = RouteGuidParam(parameters, "programId");
            var rewardId = RouteGuidParam(parameters, "rewardId");
            var request = await Request.ReadValidatedJsonBodyAsync(updateValidator, ct);
            return JsonResponses.Ok(await appService.UpdateAsync(tenantId, programId, rewardId, request, ct));
        });

        MapPatch("/{rewardId}/active", async (parameters, ct) =>
        {
            var tenantId = RequireTenantScope(RouteParam(parameters, "tenantId"));
            var programId = RouteGuidParam(parameters, "programId");
            var rewardId = RouteGuidParam(parameters, "rewardId");
            var body = await Request.ReadJsonBodyAsync<SetActiveRequest>(ct);
            return JsonResponses.Ok(await appService.SetActiveAsync(tenantId, programId, rewardId, body.IsActive, ct));
        });

        // CR 2026-09-30 (A4): moves a cashback reward out of PendingApproval — a different admin
        // than its creator (same shape as PATCH .../rules/{ruleId}/approve, CR-04).
        MapPatch("/{rewardId}/approve", async (parameters, ct) =>
        {
            var tenantId = RequireTenantScope(RouteParam(parameters, "tenantId"));
            var programId = RouteGuidParam(parameters, "programId");
            var rewardId = RouteGuidParam(parameters, "rewardId");
            var approvedBy = RequireAuthenticated().SubjectId;
            return JsonResponses.Ok(await appService.ApproveAsync(tenantId, programId, rewardId, approvedBy, ct));
        });

        MapDelete("/{rewardId}", async (parameters, ct) =>
        {
            var tenantId = RequireTenantScope(RouteParam(parameters, "tenantId"));
            var programId = RouteGuidParam(parameters, "programId");
            var rewardId = RouteGuidParam(parameters, "rewardId");
            await appService.DeleteAsync(tenantId, programId, rewardId, ct);
            return JsonResponses.NoContent();
        });
    }

    private sealed record SetActiveRequest(bool IsActive);
}
