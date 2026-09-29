using System.Text.Json;
using dEngage.Loyalty.RuleEngine.Models;
using dEngage.Loyalty.Schema;
using dEngage.Loyalty.Shared;
using dEngage.Loyalty.Shared.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace dEngage.Loyalty.RuleEngine.Processing;

// CR-10/CR-09 (docs/scope-change-rules A10 guarantee #10, A11): the deterministic scheduled
// trigger for `birthdaybonus` — same self-scheduling BackgroundService + idempotent-invocation
// shape as PointsExpirationJob/StreakMaintenanceJob. Calls IRuleEngine.ProcessEventAsync
// directly rather than round-tripping through RabbitMQ/EventInbox — birthdaybonus is
// Source.Scheduled (never externally publishable, see EventTypes.IsExternallyPublishable), so
// there is no inbound message to consume; idempotency instead comes from the deterministic
// eventId "birthday:{contactKey}:{year}" feeding the SAME ledger idempotency-key mechanism
// every other posting already relies on (a second run this year is a no-op at LedgerPoster,
// not merely at this job).
//
// Leap-day policy: a customer born on Feb 29 receives their bonus on Feb 28 in a non-leap year.
public sealed class BirthdayBonusJob(LoyaltyDbContext db, IRuleEngine ruleEngine, ILogger<BirthdayBonusJob> logger)
{
    public async Task<int> RunAsync(CancellationToken ct = default)
    {
        var today = DateTime.UtcNow;
        var monthDay = today.ToString("MM-dd");
        var candidateKeys = new List<string> { monthDay };
        if (monthDay == "02-28" && !DateTime.IsLeapYear(today.Year))
            candidateKeys.Add("02-29");

        var birthdays = await db.CustomerBirthdays
            .Where(b => candidateKeys.Contains(b.MonthDay))
            .ToListAsync(ct);
        if (birthdays.Count == 0) return 0;

        var tenantGuids = birthdays.Select(b => b.TenantId).Distinct().ToList();
        var tenantSlugs = await db.Tenants
            .Where(t => tenantGuids.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => t.Slug, ct);

        var year = today.Year;
        var fired = 0;

        foreach (var group in birthdays.GroupBy(b => b.TenantId))
        {
            ct.ThrowIfCancellationRequested();
            if (!tenantSlugs.TryGetValue(group.Key, out var tenantSlug))
            {
                logger.LogWarning("BirthdayBonusJob: tenant {TenantId} not found — skipping {Count} birthdays", group.Key, group.Count());
                continue;
            }

            var programs = await db.Programs
                // 1.3.CL item 8: only published + active programs run — a draft is never evaluated.
                .Where(p => p.TenantId == group.Key && p.Status == ProgramStatus.Active
                            && p.PublicationStatus == ProgramPublicationStatus.Published)
                .Select(p => p.Id)
                .ToListAsync(ct);
            if (programs.Count == 0) continue;

            foreach (var birthday in group)
            {
                var eventId = $"birthday:{birthday.ContactKey}:{year}";
                var evt = new EvaluationEvent
                {
                    EventType = EventTypes.BirthdayBonus,
                    ContactKey = birthday.ContactKey,
                    OccurredAt = today,
                    Data = JsonSerializer.SerializeToElement(new { contact_key = birthday.ContactKey })
                };

                foreach (var programId in programs)
                {
                    await ruleEngine.ProcessEventAsync(tenantSlug, programId, eventId, evt, ct);
                    fired++;
                }
            }
        }

        logger.LogInformation("BirthdayBonusJob: evaluated {Fired} customer/program pairs for {MonthDay}", fired, monthDay);
        return fired;
    }
}
