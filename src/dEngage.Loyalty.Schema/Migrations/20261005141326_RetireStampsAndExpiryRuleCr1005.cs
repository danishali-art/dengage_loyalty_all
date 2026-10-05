using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace dEngage.Loyalty.Schema.Migrations
{
    /// <inheritdoc />
    public partial class RetireStampsAndExpiryRuleCr1005 : Migration
    {
        // CR 2026-10-05 (D1, D15): StampRule, ExpiryRule, the points.expired trigger and STAMP
        // targets are retired. Rules and streak campaigns using them are disabled, never deleted
        // (rule versions untouched). No balance changes: ledger rows are not touched, and
        // ExpiryRule / points.expired never fired (nothing publishes points.expired). A status
        // change is not a version bump (same as RulesAppService.SetStatusAsync); updated_at is
        // touched so RuleSyncService's delta reloads the Redis caches.
        //
        // D7: the stamp reward link (stamp_account_type_id, its FK and indexes) is dropped. Its
        // only rows are retired stamp_completion rewards, deactivated by CR 2026-09-30.
        private const string AffectedRules = """
            status IN ('active', 'pending_approval')
            AND (type IN ('StampRule', 'ExpiryRule')
              OR trigger = 'points.expired'
              OR target_account_type_id IN (SELECT id FROM account_types WHERE type = 'STAMP'))
            """;

        private const string AffectedCampaigns = """
            status = 'active' AND trigger = 'points.expired'
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Same effect as IProgramChangeTracker.MarkChangedAsync for an API status change.
            // Runs first, while the affected rows still match. Trailing ";" required by the
            // idempotent deploy script's DO $EF$ wrapper.
            migrationBuilder.Sql($"""
                UPDATE programs
                SET has_unpublished_changes = true
                WHERE publication_status = 'published'
                  AND (id IN (SELECT program_id FROM rules WHERE {AffectedRules})
                    OR id IN (SELECT program_id FROM streak_campaigns WHERE {AffectedCampaigns}));
                """);

            migrationBuilder.Sql($"""
                UPDATE rules
                SET status     = 'disabled',
                    updated_at = now()
                WHERE {AffectedRules};
                """);

            migrationBuilder.Sql($"""
                UPDATE streak_campaigns
                SET status     = 'disabled',
                    updated_at = now()
                WHERE {AffectedCampaigns};
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_reward_definitions_account_types_stamp_account_type_id",
                table: "reward_definitions");

            migrationBuilder.DropIndex(
                name: "IX_reward_definitions_stamp_account_type_id",
                table: "reward_definitions");

            migrationBuilder.DropIndex(
                name: "ux_reward_definitions_active_stamp",
                table: "reward_definitions");

            migrationBuilder.DropColumn(
                name: "stamp_account_type_id",
                table: "reward_definitions");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Restores the empty column, FK and indexes only. The dropped stamp links and the
            // disabled rules/campaigns are not restored: which rows were active before isn't
            // recorded, and re-enabling is an admin decision (same as BurnTriggerRuleTypesCr0930).
            migrationBuilder.AddColumn<Guid>(
                name: "stamp_account_type_id",
                table: "reward_definitions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_reward_definitions_stamp_account_type_id",
                table: "reward_definitions",
                column: "stamp_account_type_id");

            migrationBuilder.CreateIndex(
                name: "ux_reward_definitions_active_stamp",
                table: "reward_definitions",
                columns: new[] { "tenant_id", "stamp_account_type_id" },
                unique: true,
                filter: "acquisition = 'stamp_completion' AND is_active");

            migrationBuilder.AddForeignKey(
                name: "FK_reward_definitions_account_types_stamp_account_type_id",
                table: "reward_definitions",
                column: "stamp_account_type_id",
                principalTable: "account_types",
                principalColumn: "id");
        }
    }
}
