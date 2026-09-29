using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace dEngage.Loyalty.Schema.Migrations
{
    /// <inheritdoc />
    public partial class AddRuleFireAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "rule_fire_audit",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    rule_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rule_version = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    source_event_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    contact_key = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    conditions_snapshot = table.Column<string>(type: "jsonb", nullable: true),
                    calculation_snapshot = table.Column<string>(type: "jsonb", nullable: false),
                    resulting_delta = table.Column<decimal>(type: "numeric(20,4)", nullable: false),
                    ledger_entry_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rule_fire_audit", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "idx_rule_fire_audit_tenant_rule_date",
                table: "rule_fire_audit",
                columns: new[] { "tenant_id", "rule_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ux_rule_fire_audit_source_event_rule",
                table: "rule_fire_audit",
                columns: new[] { "tenant_id", "source_event_id", "rule_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "rule_fire_audit");
        }
    }
}
