using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace dEngage.Loyalty.Schema.Migrations
{
    /// <inheritdoc />
    public partial class AddStreakCampaigns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "rule_id",
                table: "streak_progress",
                newName: "campaign_id");

            migrationBuilder.RenameColumn(
                name: "rule_id",
                table: "streak_period_state",
                newName: "campaign_id");

            migrationBuilder.RenameColumn(
                name: "rule_id",
                table: "streak_log",
                newName: "campaign_id");

            migrationBuilder.RenameColumn(
                name: "rule_id",
                table: "streak_applied_event",
                newName: "campaign_id");

            migrationBuilder.CreateTable(
                name: "streak_campaigns",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    program_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    trigger = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    target_account_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    conditions = table.Column<string>(type: "jsonb", nullable: true),
                    config = table.Column<string>(type: "jsonb", nullable: false),
                    active_from = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    active_to = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_streak_campaigns", x => x.id);
                    table.ForeignKey(
                        name: "FK_streak_campaigns_account_types_target_account_type_id",
                        column: x => x.target_account_type_id,
                        principalTable: "account_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_streak_campaigns_programs_program_id",
                        column: x => x.program_id,
                        principalTable: "programs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "idx_streak_campaigns_tenant_program_status",
                table: "streak_campaigns",
                columns: new[] { "tenant_id", "program_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_streak_campaigns_program_id",
                table: "streak_campaigns",
                column: "program_id");

            migrationBuilder.CreateIndex(
                name: "IX_streak_campaigns_target_account_type_id",
                table: "streak_campaigns",
                column: "target_account_type_id");

            // Data migration: carry every existing StreakRule row over into its own table,
            // preserving the same id — streak_progress/streak_period_state/streak_log/
            // streak_applied_event all reference this id (now via campaign_id, renamed above),
            // so an in-flight streak keeps resolving with zero discontinuity.
            migrationBuilder.Sql("""
                INSERT INTO streak_campaigns (
                    id, tenant_id, program_id, name, trigger, target_account_type_id,
                    conditions, config, active_from, active_to, status, created_at, updated_at
                )
                SELECT
                    id, tenant_id, program_id, name, trigger, target_account_type_id,
                    conditions, streak_config, active_from, active_to, status, created_at, updated_at
                FROM rules
                WHERE type = 'StreakRule';
                """);

            // rules_soft_delete_trg (see RulesSoftDeleteTrigger migration) intercepts a physical
            // DELETE on any row that isn't already status='deleted' and turns it into a soft-delete
            // UPDATE instead — so an active/disabled StreakRule row would silently survive a plain
            // DELETE here. Soft-delete first (a no-op for rows already deleted), then the physical
            // DELETE is guaranteed to go through for every row, matching RulesAppService.DeleteAsync's
            // documented two-step reasoning for the same trigger.
            migrationBuilder.Sql("UPDATE rules SET status = 'deleted', updated_at = NOW() WHERE type = 'StreakRule' AND status <> 'deleted';");
            migrationBuilder.Sql("DELETE FROM rules WHERE type = 'StreakRule';");

            migrationBuilder.DropColumn(
                name: "streak_config",
                table: "rules");
        }

        /// <inheritdoc />
        // Note: structural rollback only — does not re-insert the migrated rows back into
        // `rules` (their id/type/status would collide with whatever the app created against
        // streak_campaigns in the meantime). Restore from a backup if data recovery is needed.
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "streak_campaigns");

            migrationBuilder.RenameColumn(
                name: "campaign_id",
                table: "streak_progress",
                newName: "rule_id");

            migrationBuilder.RenameColumn(
                name: "campaign_id",
                table: "streak_period_state",
                newName: "rule_id");

            migrationBuilder.RenameColumn(
                name: "campaign_id",
                table: "streak_log",
                newName: "rule_id");

            migrationBuilder.RenameColumn(
                name: "campaign_id",
                table: "streak_applied_event",
                newName: "rule_id");

            migrationBuilder.AddColumn<string>(
                name: "streak_config",
                table: "rules",
                type: "jsonb",
                nullable: true);
        }
    }
}
