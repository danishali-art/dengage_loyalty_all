using dEngage.Loyalty.Schema;
using dEngage.Loyalty.Shared;

namespace dEngage.Loyalty.Api.Programs;

// 1.3.CL item 8: any change to a published program or its nested config (account types, tiers,
// rewards, rules, card buckets, streak campaigns) marks it as having unpublished changes until
// the next Publish. Like IConfigVersionService.StageAsync it only stages the change on the
// shared scoped LoyaltyDbContext — the caller's own SaveChangesAsync commits the flag together
// with the edit, so the two can never disagree.
public interface IProgramChangeTracker
{
    // Callers must already have checked the program belongs to the tenant (RequireProgramAsync
    // or an ownership-checked Find), so this looks it up by id alone.
    Task MarkChangedAsync(Guid programId, CancellationToken ct);
}

public sealed class ProgramChangeTracker(LoyaltyDbContext db) : IProgramChangeTracker
{
    public async Task MarkChangedAsync(Guid programId, CancellationToken ct)
    {
        var program = await db.Programs.FindAsync([programId], ct);
        if (program is { PublicationStatus: ProgramPublicationStatus.Published })
            program.HasUnpublishedChanges = true;
    }
}
