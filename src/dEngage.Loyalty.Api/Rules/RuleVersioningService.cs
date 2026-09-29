using dEngage.Loyalty.Schema;
using RuleEntity = dEngage.Loyalty.Schema.Entities.Rule;
using RuleVersionEntity = dEngage.Loyalty.Schema.Entities.RuleVersion;

namespace dEngage.Loyalty.Api.Rules;

// CR-09 (docs/scope-change-rules A10 guarantee #7): shared by RulesAppService and
// CardBucketsAppService — both edit `Rule` rows (a Card Bucket IS a FixedBonusRule row), so
// both need the same "archive current state, then bump" behavior on every edit.
//
// RuleVersion only ever stores SUPERSEDED versions — the current version's content lives in
// the `Rule` row itself (Rule.CurrentVersion says which number that is), the same shape every
// other "history" table in this schema (ConfigVersion, RuleFireAudit) already uses. A rule
// that's never been edited has no RuleVersion rows at all, which is correct, not a gap: its
// only-ever version is right there in `Rule`.
public interface IRuleVersioningService
{
    // Call BEFORE mutating `entity` — archives its PRE-edit state as the version it's currently
    // at, then increments entity.CurrentVersion. Does not SaveChanges; the caller's
    // AddAsync/SaveChangesAsync (after applying the edit) covers this too.
    Task ArchiveAndBumpAsync(Guid tenantGuid, RuleEntity entity, CancellationToken ct);
}

public sealed class RuleVersioningService(LoyaltyDbContext db) : IRuleVersioningService
{
    public Task ArchiveAndBumpAsync(Guid tenantGuid, RuleEntity entity, CancellationToken ct)
    {
        db.RuleVersions.Add(new RuleVersionEntity
        {
            Id = UUIDNext.Uuid.NewDatabaseFriendly(UUIDNext.Database.PostgreSql),
            TenantId = tenantGuid,
            RuleId = entity.Id,
            VersionNumber = entity.CurrentVersion,
            Name = entity.Name,
            Type = entity.Type,
            Trigger = entity.Trigger,
            Conditions = entity.Conditions,
            Calculation = entity.Calculation,
            TargetAccountTypeId = entity.TargetAccountTypeId,
            Limits = entity.Limits,
            Configuration = entity.Configuration,
            Priority = entity.Priority,
            Stackable = entity.Stackable,
            ExclusivityGroup = entity.ExclusivityGroup,
            StackMode = entity.StackMode,
            ActiveFrom = entity.ActiveFrom,
            ActiveTo = entity.ActiveTo,
            EffectiveFrom = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        });
        entity.CurrentVersion++;
        return Task.CompletedTask;
    }
}
