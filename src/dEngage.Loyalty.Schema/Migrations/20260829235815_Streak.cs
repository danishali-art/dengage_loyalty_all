using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace dEngage.Loyalty.Schema.Migrations
{
    /// <inheritdoc />
    public partial class Streak : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "streak_config",
                table: "rules",
                type: "jsonb",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "streak_applied_event",
                columns: table => new
                {
                    tenant_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    rule_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    applied_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_streak_applied_event", x => new { x.tenant_id, x.rule_id, x.event_id });
                });

            migrationBuilder.CreateTable(
                name: "streak_log",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    rule_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contact_key = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    completion_no = table.Column<int>(type: "integer", nullable: false),
                    completed_period = table.Column<DateOnly>(type: "date", nullable: false),
                    periods = table.Column<int>(type: "integer", nullable: false),
                    reward_kind = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    reward_ref = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    source_event_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_streak_log", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "streak_period_state",
                columns: table => new
                {
                    tenant_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    rule_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contact_key = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    period_start = table.Column<DateOnly>(type: "date", nullable: false),
                    agg_sum = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    agg_count = table.Column<int>(type: "integer", nullable: false),
                    met = table.Column<bool>(type: "boolean", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_streak_period_state", x => new { x.tenant_id, x.rule_id, x.contact_key, x.period_start });
                });

            migrationBuilder.CreateTable(
                name: "streak_progress",
                columns: table => new
                {
                    tenant_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    rule_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contact_key = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    streak_count = table.Column<int>(type: "integer", nullable: false),
                    last_met_period = table.Column<DateOnly>(type: "date", nullable: true),
                    completions = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_streak_progress", x => new { x.tenant_id, x.rule_id, x.contact_key });
                });

            migrationBuilder.CreateIndex(
                name: "ux_streak_log_completion",
                table: "streak_log",
                columns: new[] { "tenant_id", "rule_id", "contact_key", "completion_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_streak_period_state_rule_period",
                table: "streak_period_state",
                columns: new[] { "tenant_id", "rule_id", "period_start" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "streak_applied_event");

            migrationBuilder.DropTable(
                name: "streak_log");

            migrationBuilder.DropTable(
                name: "streak_period_state");

            migrationBuilder.DropTable(
                name: "streak_progress");

            migrationBuilder.DropColumn(
                name: "streak_config",
                table: "rules");
        }
    }
}
