using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace dEngage.Loyalty.Schema.Migrations
{
    /// <inheritdoc />
    public partial class DisableTestModeEarnRulesCr1006 : Migration
    {
        // CR 2026-10-06 D20/D21 (docs/scope-changes/2026-10-05-rule-config-limits-by-trigger.md
        // §3.10): test mode was removed from every rule, and the engine no longer honours a stored
        // testMode flag. An earn rule that had it on evaluated and audited but paid nothing — it
        // would start paying real points and cash the moment this deploys. Such rules are disabled
        // here — never deleted — so an admin switches each one on deliberately. Data only, no
        // balance changes. Redeem / transfer / reversal rules are left alone: their flag was
        // already ignored, so nothing changes for them (listed by the pre-deploy query instead).
        // Same pattern as DisableLegacyBurnRulesCr1005: updated_at is touched so RuleSyncService
        // reloads the rule cache, and a published program shows unpublished changes.
        private const string Affected = """
            status IN ('active', 'pending_approval')
            AND type IN ('SpendRule', 'FixedBonusRule', 'ManualAdjustmentRule')
            AND configuration->>'testMode' = 'true'
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Runs first, while the affected rows still match.
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
            // and switching a rule on is an admin decision (PATCH .../status), not a schema step.
            // Intentionally a no-op so a schema rollback past this point works.
        }
    }
}
