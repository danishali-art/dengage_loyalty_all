using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace dEngage.Loyalty.Schema.Migrations
{
    /// <inheritdoc />
    // TenantIdGuidForeignKeys used ADD COLUMN + DROP COLUMN + RENAME to retype tenant_id on 15
    // tables — DROP COLUMN silently drops any index/unique-constraint that included it, and that
    // migration only recreated the FK, not these. Restores them exactly as each table's
    // Configuration.cs still declares (EF's own model diff sees no change here, since the C#
    // model was never wrong — only the live schema drifted from it via that migration's raw SQL —
    // so this migration is hand-written, not scaffolded from a model change).
    public partial class RestoreTenantScopedIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "idx_account_types_tenant_program",
                table: "account_types",
                columns: new[] { "tenant_id", "program_id" });

            migrationBuilder.CreateIndex(
                name: "uq_customer_accounts_tenant_contact_accounttype",
                table: "customer_accounts",
                columns: new[] { "tenant_id", "contact_key", "account_type_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_customer_accounts_tenant_contact",
                table: "customer_accounts",
                columns: new[] { "tenant_id", "contact_key" });

            migrationBuilder.CreateIndex(
                name: "ux_outbox_dedup",
                table: "outbox_events",
                columns: new[] { "tenant_id", "dedup_key" },
                unique: true,
                filter: "dedup_key IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "idx_programs_tenant_id",
                table: "programs",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "idx_reward_definitions_tenant_program",
                table: "reward_definitions",
                columns: new[] { "tenant_id", "program_id" });

            migrationBuilder.CreateIndex(
                name: "uq_reward_definitions_tenant_program_name",
                table: "reward_definitions",
                columns: new[] { "tenant_id", "program_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_reward_definitions_active_stamp",
                table: "reward_definitions",
                columns: new[] { "tenant_id", "stamp_account_type_id" },
                unique: true,
                filter: "acquisition = 'stamp_completion' AND is_active");

            migrationBuilder.CreateIndex(
                name: "idx_reward_log_tenant_contact_status",
                table: "reward_log",
                columns: new[] { "tenant_id", "contact_key", "status" });

            migrationBuilder.CreateIndex(
                name: "ux_reward_log_source_event",
                table: "reward_log",
                columns: new[] { "tenant_id", "account_type_id", "source_event_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_rules_tenant_program_status",
                table: "rules",
                columns: new[] { "tenant_id", "program_id", "status" });

            migrationBuilder.CreateIndex(
                name: "idx_rule_fire_audit_tenant_rule_date",
                table: "rule_fire_audit",
                columns: new[] { "tenant_id", "rule_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ux_rule_fire_audit_source_event_rule",
                table: "rule_fire_audit",
                columns: new[] { "tenant_id", "source_event_id", "rule_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_streak_campaigns_tenant_program_status",
                table: "streak_campaigns",
                columns: new[] { "tenant_id", "program_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ux_streak_log_completion",
                table: "streak_log",
                columns: new[] { "tenant_id", "campaign_id", "contact_key", "completion_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_streak_period_state_rule_period",
                table: "streak_period_state",
                columns: new[] { "tenant_id", "campaign_id", "period_start" });

            migrationBuilder.CreateIndex(
                name: "idx_tier_definitions_tenant_program",
                table: "tier_definitions",
                columns: new[] { "tenant_id", "program_id" });

            migrationBuilder.CreateIndex(
                name: "idx_tier_upgrade_log_tenant_contact",
                table: "tier_upgrade_log",
                columns: new[] { "tenant_id", "contact_key" });

            migrationBuilder.CreateIndex(
                name: "ux_tier_upgrade_log_source_event",
                table: "tier_upgrade_log",
                columns: new[] { "tenant_id", "contact_key", "source_event_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "idx_account_types_tenant_program", table: "account_types");
            migrationBuilder.DropIndex(name: "uq_customer_accounts_tenant_contact_accounttype", table: "customer_accounts");
            migrationBuilder.DropIndex(name: "idx_customer_accounts_tenant_contact", table: "customer_accounts");
            migrationBuilder.DropIndex(name: "ux_outbox_dedup", table: "outbox_events");
            migrationBuilder.DropIndex(name: "idx_programs_tenant_id", table: "programs");
            migrationBuilder.DropIndex(name: "idx_reward_definitions_tenant_program", table: "reward_definitions");
            migrationBuilder.DropIndex(name: "uq_reward_definitions_tenant_program_name", table: "reward_definitions");
            migrationBuilder.DropIndex(name: "ux_reward_definitions_active_stamp", table: "reward_definitions");
            migrationBuilder.DropIndex(name: "idx_reward_log_tenant_contact_status", table: "reward_log");
            migrationBuilder.DropIndex(name: "ux_reward_log_source_event", table: "reward_log");
            migrationBuilder.DropIndex(name: "idx_rules_tenant_program_status", table: "rules");
            migrationBuilder.DropIndex(name: "idx_rule_fire_audit_tenant_rule_date", table: "rule_fire_audit");
            migrationBuilder.DropIndex(name: "ux_rule_fire_audit_source_event_rule", table: "rule_fire_audit");
            migrationBuilder.DropIndex(name: "idx_streak_campaigns_tenant_program_status", table: "streak_campaigns");
            migrationBuilder.DropIndex(name: "ux_streak_log_completion", table: "streak_log");
            migrationBuilder.DropIndex(name: "idx_streak_period_state_rule_period", table: "streak_period_state");
            migrationBuilder.DropIndex(name: "idx_tier_definitions_tenant_program", table: "tier_definitions");
            migrationBuilder.DropIndex(name: "idx_tier_upgrade_log_tenant_contact", table: "tier_upgrade_log");
            migrationBuilder.DropIndex(name: "ux_tier_upgrade_log_source_event", table: "tier_upgrade_log");
        }
    }
}
