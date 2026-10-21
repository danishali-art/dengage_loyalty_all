using FluentValidation;
using dEngage.Loyalty.Api.Framework.Controllers;
using dEngage.Loyalty.Api.Framework.Events;
using dEngage.Loyalty.Api.Framework.Json;
using dEngage.Loyalty.Api.Framework.Validation;
using HttpStatusCode = System.Net.HttpStatusCode;
using dEngage.Loyalty.Contract;
using Microsoft.AspNetCore.Mvc;

namespace dEngage.Loyalty.Api.Events;

// /api/v1/tenants/{tenantId}/events — the portal's side of events (admin JWT): the event simulator
// and the event type list. Server-to-server ingestion (API key) is served by Public.Api
// (platform alignment D9), on the same paths.
[Route("api/v1/tenants/{tenantId}/events")]
public sealed class EventsController(
    IEventsAppService appService,
    IValidator<GenericEventRequest> genericEventValidator) : LoyaltyControllerBase
{
    // Admin-JWT counterpart to Public.Api's /generic — same publish path, but for
    // tenant_admin/platform_admin testing a rule from the portal, who won't have a raw API key to
    // hand. Returns 202 Accepted, like every ingestion route: accepted for processing, not processed.
    [HttpPost("simulate")]
    public async Task<IActionResult> Simulate(CancellationToken ct)
    {
        var tenantId = RequireTenantScope(RouteParam("tenantId"));
        var request = await Request.ReadValidatedJsonBodyAsync(genericEventValidator, ct);
        return Accepted(await appService.PublishAsync(tenantId, request.EventType, request.Data, IdempotencyKey(), ct));
    }

    [HttpGet("types")]
    public IActionResult GetEventTypes()
    {
        RequireTenantScope(RouteParam("tenantId"));
        return JsonResponses.Ok(appService.GetEventTypes());
    }

    // Lets the portal's event simulator poll a simulated event's outcome. Public.Api serves the
    // same lookup to API-key callers.
    [HttpGet("{eventId}")]
    public async Task<IActionResult> GetStatus(CancellationToken ct)
    {
        var tenantId = RequireTenantScope(RouteParam("tenantId"));
        var eventId = RouteParam("eventId");
        return JsonResponses.Ok(await appService.GetStatusAsync(tenantId, eventId, ct));
    }

    private string? IdempotencyKey() => Request.Headers["Idempotency-Key"].FirstOrDefault();

    private static IActionResult Accepted(EventAcceptedResponse body) => JsonResponses.Ok(body, HttpStatusCode.Accepted);
}
