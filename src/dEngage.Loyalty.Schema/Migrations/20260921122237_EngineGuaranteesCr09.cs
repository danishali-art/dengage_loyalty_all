using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace dEngage.Loyalty.Schema.Migrations
{
    /// <inheritdoc />
    public partial class EngineGuaranteesCr09 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Existing rules are treated as "version 1" too — consistent with new rules'
            // CurrentVersion default (RuleEntity.CurrentVersion = 1) rather than starting
            // pre-existing rows at a nonsensical version 0.
            migrationBuilder.AddColumn<int>(
                name: "current_version",
                table: "rules",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            // Was a timestamp-only proxy (the fire time, not a real version number) — there is
            // no meaningful automatic conversion from that to an integer version, so this drops
            // and replaces rather than casting. Existing audit rows become "version 1" for the
            // same reason existing rules do above: consistent with the new default, not a claim
            // about which version actually fired historically (that history wasn't tracked).
            migrationBuilder.DropColumn(
                name: "rule_version",
                table: "rule_fire_audit");

            migrationBuilder.AddColumn<int>(
                name: "rule_version",
                table: "rule_fire_audit",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateTable(
                name: "rule_limit_counters",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rule_id = table.Column<Guid>(type: "uuid", nullable: false),
                    counter_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    period_key = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    value = table.Column<decimal>(type: "numeric(20,4)", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rule_limit_counters", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "rule_versions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rule_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_number = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    trigger = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    conditions = table.Column<string>(type: "jsonb", nullable: true),
                    calculation = table.Column<string>(type: "jsonb", nullable: false),
                    target_account_type_id = table.Column<Guid>(type: "uuid", nullable: true),
                    limits = table.Column<string>(type: "jsonb", nullable: true),
                    configuration = table.Column<string>(type: "jsonb", nullable: true),
                    priority = table.Column<int>(type: "integer", nullable: false),
                    stackable = table.Column<bool>(type: "boolean", nullable: false),
                    exclusivity_group = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    stack_mode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    active_from = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    active_to = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    effective_from = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rule_versions", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ux_rule_limit_counters_key",
                table: "rule_limit_counters",
                columns: new[] { "tenant_id", "rule_id", "counter_type", "period_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_rule_versions_tenant_rule_version",
                table: "rule_versions",
                columns: new[] { "tenant_id", "rule_id", "version_number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "rule_limit_counters");

            migrationBuilder.DropTable(
                name: "rule_versions");

            migrationBuilder.DropColumn(
                name: "current_version",
                table: "rules");

            migrationBuilder.DropColumn(
                name: "rule_version",
                table: "rule_fire_audit");

            migrationBuilder.AddColumn<DateTime>(
                name: "rule_version",
                table: "rule_fire_audit",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        }
    }
}
