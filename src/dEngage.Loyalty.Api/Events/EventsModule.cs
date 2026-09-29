using FluentValidation;
using dEngage.Loyalty.Api.Framework.Json;
using dEngage.Loyalty.Api.Framework.Modules;
using dEngage.Loyalty.Api.Framework.Validation;
using dEngage.Loyalty.Shared.Events;
using HttpStatusCode = System.Net.HttpStatusCode;

namespace dEngage.Loyalty.Api.Events;

// /api/v1/tenants/{tenantId}/events — API-key auth only (server-to-server ingestion gateway).
// Every route returns 202 Accepted, never 200: publishing means "accepted for processing," not
// "the business outcome is known" (plan §4 — e.g. reward.purchase can be fully processed yet
// represent a failed business outcome).
public sealed class EventsModule : TenantScopedModule
{
    public EventsModule(
        IEventsAppService appService,
        IValidator<OrderCreatedRequest> orderCreatedValidator,
        IValidator<OrderRefundedRequest> orderRefundedValidator,
        IValidator<CashAddedRequest> cashAddedValidator,
        IValidator<CashSpentRequest> cashSpentValidator,
        IValidator<PointsRedeemRequest> pointsRedeemValidator,
        IValidator<PointsTransferRequest> pointsTransferValidator,
        IValidator<RewardPurchaseRequest> rewardPurchaseValidator,
        IValidator<GenericEventRequest> genericEventValidator)
        : base("/api/v1/tenants/{tenantId}/events")
    {
        MapPost("/order-created", async (parameters, ct) =>
        {
            var tenantId = RequireTenantScope(RouteParam(parameters, "tenantId"));
            RequireApiKeyPrincipal();
            var request = await Request.ReadValidatedJsonBodyAsync(orderCreatedValidator, ct);
            return Accepted(await appService.PublishAsync(tenantId, EventTypes.OrderCreated, request, IdempotencyKey(), ct));
        });

        MapPost("/order-refunded", async (parameters, ct) =>
        {
            var tenantId = RequireTenantScope(RouteParam(parameters, "tenantId"));
            RequireApiKeyPrincipal();
            var request = await Request.ReadValidatedJsonBodyAsync(orderRefundedValidator, ct);
            return Accepted(await appService.PublishAsync(tenantId, EventTypes.OrderRefunded, request, IdempotencyKey(), ct));
        });

        MapPost("/cash-added", async (parameters, ct) =>
        {
            var tenantId = RequireTenantScope(RouteParam(parameters, "tenantId"));
            RequireApiKeyPrincipal();
            var request = await Request.ReadValidatedJsonBodyAsync(cashAddedValidator, ct);
            return Accepted(await appService.PublishAsync(tenantId, EventTypes.CashAdded, request, IdempotencyKey(), ct));
        });

        MapPost("/cash-spent", async (parameters, ct) =>
        {
            var tenantId = RequireTenantScope(RouteParam(parameters, "tenantId"));
            RequireApiKeyPrincipal();
            var request = await Request.ReadValidatedJsonBodyAsync(cashSpentValidator, ct);
            return Accepted(await appService.PublishAsync(tenantId, EventTypes.CashSpent, request, IdempotencyKey(), ct));
        });

        MapPost("/points-redeem", async (parameters, ct) =>
        {
            var tenantId = RequireTenantScope(RouteParam(parameters, "tenantId"));
            RequireApiKeyPrincipal();
            var request = await Request.ReadValidatedJsonBodyAsync(pointsRedeemValidator, ct);
            return Accepted(await appService.PublishAsync(tenantId, EventTypes.PointsRedeem, request, IdempotencyKey(), ct));
        });

        MapPost("/points-transfer", async (parameters, ct) =>
        {
            var tenantId = RequireTenantScope(RouteParam(parameters, "tenantId"));
            RequireApiKeyPrincipal();
            var request = await Request.ReadValidatedJsonBodyAsync(pointsTransferValidator, ct);
            return Accepted(await appService.PublishAsync(tenantId, EventTypes.PointsTransfer, request, IdempotencyKey(), ct));
        });

        MapPost("/reward-purchase", async (parameters, ct) =>
        {
            var tenantId = RequireTenantScope(RouteParam(parameters, "tenantId"));
            RequireApiKeyPrincipal();
            var request = await Request.ReadValidatedJsonBodyAsync(rewardPurchaseValidator, ct);
            return Accepted(await appService.PublishAsync(tenantId, EventTypes.RewardPurchase, request, IdempotencyKey(), ct));
        });

        MapPost("/generic", async (parameters, ct) =>
        {
            var tenantId = RequireTenantScope(RouteParam(parameters, "tenantId"));
            RequireApiKeyPrincipal();
            var request = await Request.ReadValidatedJsonBodyAsync(genericEventValidator, ct);
            return Accepted(await appService.PublishAsync(tenantId, request.EventType, request.Data, IdempotencyKey(), ct));
        });

        // Admin-JWT counterpart to /generic — same publish path, but for tenant_admin/platform_admin
        // testing a rule from the portal, who won't have a raw API key to hand. Production
        // server-to-server ingestion stays on /generic, strictly API-key gated.
        MapPost("/simulate", async (parameters, ct) =>
        {
            var tenantId = RequireTenantScope(RouteParam(parameters, "tenantId"));
            var request = await Request.ReadValidatedJsonBodyAsync(genericEventValidator, ct);
            return Accepted(await appService.PublishAsync(tenantId, request.EventType, request.Data, IdempotencyKey(), ct));
        });

        MapGet("/types", (parameters, _) =>
        {
            RequireTenantScope(RouteParam(parameters, "tenantId"));
            return Task.FromResult<object>(JsonResponses.Ok(appService.GetEventTypes()));
        });

        // Read-only and already tenant-scoped — left open to admin JWT too (not just API-key
        // callers) so the portal's event simulator can poll a simulated event's outcome.
        MapGet("/{eventId}", async (parameters, ct) =>
        {
            var tenantId = RequireTenantScope(RouteParam(parameters, "tenantId"));
            var eventId = RouteParam(parameters, "eventId");
            return JsonResponses.Ok(await appService.GetStatusAsync(tenantId, eventId, ct));
        });
    }

    private string? IdempotencyKey() => Request.Headers["Idempotency-Key"].FirstOrDefault();

    private static Nancy.Response Accepted(EventAcceptedResponse body) => JsonResponses.Ok(body, HttpStatusCode.Accepted);
}
