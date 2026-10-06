using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace dEngage.Loyalty.Schema.Migrations
{
    /// <inheritdoc />
    public partial class DisableLegacyBurnRulesCr1005 : Migration
    {
        // CR 2026-10-05 (R-O11): points.redeem / points.transfer now always run on a rule, picked
        // automatically by wallet and priority. Every redeem / transfer rule saved before was
        // saved under the old model (redeem with `ratio` and no cash wallet, transfer with no
        // `maxPerDay`) and has never posted anything (the handlers read the wallet settings and
        // the engine read "amount", not "points_amount"). Disabled here — never deleted — so none
        // starts paying out on deploy; an admin completes and re-enables each one deliberately
        // (RulesAppService.SetStatusAsync refuses an incomplete one). Data only, no balance changes.
        //
        // A status change is not a version bump (RulesAppService.SetStatusAsync doesn't write a
        // rule_versions row either). updated_at is touched so RuleSyncService's 30s delta reloads
        // the Redis rule cache. List the affected rows first with the pre-deploy query in
        // docs/scope-changes/2026-10-05-burn-rules-dynamic-reward-purchase.md (§7).
        private const string Affected = """
            status IN ('active', 'pending_approval')
            AND type IN ('RedemptionRule', 'TransferRule')
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
