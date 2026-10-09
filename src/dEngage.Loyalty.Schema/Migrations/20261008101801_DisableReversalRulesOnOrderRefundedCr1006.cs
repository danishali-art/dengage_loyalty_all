using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace dEngage.Loyalty.Schema.Migrations
{
    /// <inheritdoc />
    public partial class DisableReversalRulesOnOrderRefundedCr1006 : Migration
    {
        // CR 2026-10-06 D22 (docs/scope-changes/2026-10-05-rule-config-limits-by-trigger.md §2.8):
        // the built-in refund always reverses an order's earn, so a Reversal rule on
        // order.refunded can only reverse it a second time (confirmed by RefundPathsCr1006E2ETests).
        // order.refunded no longer accepts a Reversal rule (RuleTypeCatalog); existing ones are
        // disabled here — never deleted — so none keeps running after deploy. Data only, no balance
        // changes. Same pattern as DisableLegacyBurnRulesCr1005: updated_at is touched so
        // RuleSyncService reloads the rule cache, and a published program shows unpublished changes.
        // List the affected rows first with the pre-deploy query in the CR document (§7.2).
        private const string Affected = """
            status IN ('active', 'pending_approval')
            AND type = 'ReversalRule'
            AND trigger = 'order.refunded'
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
            // and such a rule can't be re-enabled anyway (order.refunded accepts no Reversal rule).
            // Intentionally a no-op so a schema rollback past this point works.
        }
    }
}
