using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace dEngage.Loyalty.Schema.Migrations
{
    /// <inheritdoc />
    public partial class BurnTriggerRuleTypesCr0930 : Migration
    {
        // CR 2026-09-30 item 8 (§3.9 step 1): RuleTypeCatalog now allows only TransferRule on
        // points.transfer, only RedemptionRule on points.redeem, and no rule at all on
        // reward.purchase. Rules created before that under another combination are disabled,
        // never deleted. Data only.
        //
        // No balance changes: burn rules never post today (the engine reads "amount", the burn
        // events send "points_amount" — see the CR §2.8), so disabling them removes nothing that
        // ever fired. A status change is not a version bump — RulesAppService.SetStatusAsync
        // doesn't write a rule_versions row either. updated_at is touched so RuleSyncService's
        // 30s delta reloads the Redis rule cache. List the affected rows first with the item-8
        // pre-deploy query in docs/scope-changes/2026-09-30-reward-acquisition-fulfilment.md.
        private const string Affected = """
            status IN ('active', 'pending_approval')
            AND ((trigger = 'points.transfer' AND type <> 'TransferRule')
              OR (trigger = 'points.redeem' AND type <> 'RedemptionRule')
              OR trigger = 'reward.purchase')
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Same effect as IProgramChangeTracker.MarkChangedAsync for an API status change:
            // a published program whose rules change shows unpublished changes. Runs first,
            // while the affected rows still match.
            // Trailing ";" required by the idempotent deploy script's DO $EF$ wrapper.
            migrationBuilder.Sql($"""
                UPDATE programs
                SET has_unpublished_changes = true
                WHERE publication_status = 'published'
                  AND id IN (SELECT program_id FROM rules WHERE {Affected});
                """);

            migrationBuilder.Sql($"""
                UPDATE rules
                SET status     = 'disabled',
                    updated_at = now()
                WHERE {Affected};
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Not reversible by design: which rules were active vs pending before isn't recorded,
            // and re-enabling a rule is an admin decision (PATCH .../status), not a schema step.
            // Intentionally a no-op so a schema rollback past this point works.
        }
    }
}
