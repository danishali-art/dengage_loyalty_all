using FluentValidation;
using dEngage.Loyalty.Api.Framework.Controllers;
using dEngage.Loyalty.Api.Framework.Events;
using dEngage.Loyalty.Api.Framework.Json;
using dEngage.Loyalty.Api.Framework.Validation;
using HttpStatusCode = System.Net.HttpStatusCode;
using dEngage.Loyalty.Contract;
using Microsoft.AspNetCore.Mvc;

namespace dEngage.Loyalty.Public.Api.Events;

// /api/v1/tenants/{tenantId}/events — API-key auth only (server-to-server ingestion gateway). Same
// paths as before the split (platform alignment D9); a deployment routes clients to this service
// by host name.
// Every route returns 202 Accepted, never 200: publishing means "accepted for processing," not
// "the business outcome is known" (plan §4 — e.g. reward.purchase can be fully processed yet
// represent a failed business outcome).
[Route("api/v1/tenants/{tenantId}/events")]
public sealed class EventsController(
    IEventsAppService appService,
    IValidator<OrderCreatedRequest> orderCreatedValidator,
    IValidator<OrderRefundedRequest> orderRefundedValidator,
    IValidator<CashAddedRequest> cashAddedValidator,
    IValidator<CashSpentRequest> cashSpentValidator,
    IValidator<PointsRedeemRequest> pointsRedeemValidator,
    IValidator<PointsTransferRequest> pointsTransferValidator,
    IValidator<RewardPurchaseRequest> rewardPurchaseValidator,
    IValidator<GenericEventRequest> genericEventValidator) : LoyaltyControllerBase
{
    [HttpPost("order-created")]
    public async Task<IActionResult> OrderCreated(CancellationToken ct)
    {
        var tenantId = RequireTenantScope(RouteParam("tenantId"));
        RequireApiKeyPrincipal();
        var request = await Request.ReadValidatedJsonBodyAsync(orderCreatedValidator, ct);
        return Accepted(await appService.PublishAsync(tenantId, EventTypes.OrderCreated, request, IdempotencyKey(), ct));
    }

    [HttpPost("order-refunded")]
    public async Task<IActionResult> OrderRefunded(CancellationToken ct)
    {
        var tenantId = RequireTenantScope(RouteParam("tenantId"));
        RequireApiKeyPrincipal();
        var request = await Request.ReadValidatedJsonBodyAsync(orderRefundedValidator, ct);
        return Accepted(await appService.PublishAsync(tenantId, EventTypes.OrderRefunded, request, IdempotencyKey(), ct));
    }

    [HttpPost("cash-added")]
    public async Task<IActionResult> CashAdded(CancellationToken ct)
    {
        var tenantId = RequireTenantScope(RouteParam("tenantId"));
        RequireApiKeyPrincipal();
        var request = await Request.ReadValidatedJsonBodyAsync(cashAddedValidator, ct);
        return Accepted(await appService.PublishAsync(tenantId, EventTypes.CashAdded, request, IdempotencyKey(), ct));
    }

    [HttpPost("cash-spent")]
    public async Task<IActionResult> CashSpent(CancellationToken ct)
    {
        var tenantId = RequireTenantScope(RouteParam("tenantId"));
        RequireApiKeyPrincipal();
        var request = await Request.ReadValidatedJsonBodyAsync(cashSpentValidator, ct);
        return Accepted(await appService.PublishAsync(tenantId, EventTypes.CashSpent, request, IdempotencyKey(), ct));
    }

    [HttpPost("points-redeem")]
    public async Task<IActionResult> PointsRedeem(CancellationToken ct)
    {
        var tenantId = RequireTenantScope(RouteParam("tenantId"));
        RequireApiKeyPrincipal();
        var request = await Request.ReadValidatedJsonBodyAsync(pointsRedeemValidator, ct);
        return Accepted(await appService.PublishAsync(tenantId, EventTypes.PointsRedeem, request, IdempotencyKey(), ct));
    }

    [HttpPost("points-transfer")]
    public async Task<IActionResult> PointsTransfer(CancellationToken ct)
    {
        var tenantId = RequireTenantScope(RouteParam("tenantId"));
        RequireApiKeyPrincipal();
        var request = await Request.ReadValidatedJsonBodyAsync(pointsTransferValidator, ct);
        return Accepted(await appService.PublishAsync(tenantId, EventTypes.PointsTransfer, request.ToEventData(), IdempotencyKey(), ct));
    }

    [HttpPost("reward-purchase")]
    public async Task<IActionResult> RewardPurchase(CancellationToken ct)
    {
        var tenantId = RequireTenantScope(RouteParam("tenantId"));
        RequireApiKeyPrincipal();
        var request = await Request.ReadValidatedJsonBodyAsync(rewardPurchaseValidator, ct);
        return Accepted(await appService.PublishAsync(tenantId, EventTypes.RewardPurchase, request, IdempotencyKey(), ct));
    }

    [HttpPost("generic")]
    public async Task<IActionResult> Generic(CancellationToken ct)
    {
        var tenantId = RequireTenantScope(RouteParam("tenantId"));
        RequireApiKeyPrincipal();
        var request = await Request.ReadValidatedJsonBodyAsync(genericEventValidator, ct);
        return Accepted(await appService.PublishAsync(tenantId, request.EventType, request.Data, IdempotencyKey(), ct));
    }

    // Outcome of an event this tenant sent (from the inbox). The admin Api serves the same lookup
    // to the portal's event simulator.
    [HttpGet("{eventId}")]
    public async Task<IActionResult> GetStatus(CancellationToken ct)
    {
        var tenantId = RequireTenantScope(RouteParam("tenantId"));
        RequireApiKeyPrincipal();
        var eventId = RouteParam("eventId");
        return JsonResponses.Ok(await appService.GetStatusAsync(tenantId, eventId, ct));
    }

    private string? IdempotencyKey() => Request.Headers["Idempotency-Key"].FirstOrDefault();

    private static IActionResult Accepted(EventAcceptedResponse body) => JsonResponses.Ok(body, HttpStatusCode.Accepted);
}
