using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace dEngage.Loyalty.Schema.Migrations
{
    /// <inheritdoc />
    public partial class TierSystem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "qualifying_account_type_id",
                table: "programs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "tier_expires_at",
                table: "customer_accounts",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "tier_id",
                table: "customer_accounts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "tier_period_start",
                table: "customer_accounts",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "tier_qualifying_pts",
                table: "customer_accounts",
                type: "numeric(20,4)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "tier_definitions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    program_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    display_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    min_points = table.Column<decimal>(type: "numeric(20,4)", nullable: false),
                    qualifying_days = table.Column<int>(type: "integer", nullable: true),
                    grace_days = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tier_definitions", x => x.id);
                    table.ForeignKey(
                        name: "FK_tier_definitions_programs_program_id",
                        column: x => x.program_id,
                        principalTable: "programs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tier_upgrade_log",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    contact_key = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    from_tier_id = table.Column<Guid>(type: "uuid", nullable: true),
                    to_tier_id = table.Column<Guid>(type: "uuid", nullable: false),
                    qualifying_pts = table.Column<decimal>(type: "numeric(20,4)", nullable: false),
                    source_event_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tier_upgrade_log", x => x.id);
                    table.ForeignKey(
                        name: "FK_tier_upgrade_log_tier_definitions_from_tier_id",
                        column: x => x.from_tier_id,
                        principalTable: "tier_definitions",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_tier_upgrade_log_tier_definitions_to_tier_id",
                        column: x => x.to_tier_id,
                        principalTable: "tier_definitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_programs_qualifying_account_type_id",
                table: "programs",
                column: "qualifying_account_type_id");

            migrationBuilder.CreateIndex(
                name: "IX_customer_accounts_tier_id",
                table: "customer_accounts",
                column: "tier_id");

            migrationBuilder.CreateIndex(
                name: "idx_tier_definitions_tenant_program",
                table: "tier_definitions",
                columns: new[] { "tenant_id", "program_id" });

            migrationBuilder.CreateIndex(
                name: "uq_tier_definitions_program_name",
                table: "tier_definitions",
                columns: new[] { "program_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_tier_upgrade_log_tenant_contact",
                table: "tier_upgrade_log",
                columns: new[] { "tenant_id", "contact_key" });

            migrationBuilder.CreateIndex(
                name: "IX_tier_upgrade_log_from_tier_id",
                table: "tier_upgrade_log",
                column: "from_tier_id");

            migrationBuilder.CreateIndex(
                name: "IX_tier_upgrade_log_to_tier_id",
                table: "tier_upgrade_log",
                column: "to_tier_id");

            migrationBuilder.AddForeignKey(
                name: "FK_customer_accounts_tier_definitions_tier_id",
                table: "customer_accounts",
                column: "tier_id",
                principalTable: "tier_definitions",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "FK_programs_account_types_qualifying_account_type_id",
                table: "programs",
                column: "qualifying_account_type_id",
                principalTable: "account_types",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_customer_accounts_tier_definitions_tier_id",
                table: "customer_accounts");

            migrationBuilder.DropForeignKey(
                name: "FK_programs_account_types_qualifying_account_type_id",
                table: "programs");

            migrationBuilder.DropTable(
                name: "tier_upgrade_log");

            migrationBuilder.DropTable(
                name: "tier_definitions");

            migrationBuilder.DropIndex(
                name: "IX_programs_qualifying_account_type_id",
                table: "programs");

            migrationBuilder.DropIndex(
                name: "IX_customer_accounts_tier_id",
                table: "customer_accounts");

            migrationBuilder.DropColumn(
                name: "qualifying_account_type_id",
                table: "programs");

            migrationBuilder.DropColumn(
                name: "tier_expires_at",
                table: "customer_accounts");

            migrationBuilder.DropColumn(
                name: "tier_id",
                table: "customer_accounts");

            migrationBuilder.DropColumn(
                name: "tier_period_start",
                table: "customer_accounts");

            migrationBuilder.DropColumn(
                name: "tier_qualifying_pts",
                table: "customer_accounts");
        }
    }
}
