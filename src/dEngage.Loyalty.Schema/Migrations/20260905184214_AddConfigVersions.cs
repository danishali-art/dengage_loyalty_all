using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace dEngage.Loyalty.Schema.Migrations
{
    /// <inheritdoc />
    public partial class AddConfigVersions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "uq_tier_definitions_program_name",
                table: "tier_definitions");

            migrationBuilder.DropIndex(
                name: "uq_tier_definitions_program_sort_order",
                table: "tier_definitions");

            migrationBuilder.AddColumn<string>(
                name: "status",
                table: "tier_definitions",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "active");

            migrationBuilder.CreateTable(
                name: "complaints",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    program_id = table.Column<Guid>(type: "uuid", nullable: true),
                    customer_key = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    subject = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "open"),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    resolved_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_complaints", x => x.id);
                    table.ForeignKey(
                        name: "FK_complaints_programs_program_id",
                        column: x => x.program_id,
                        principalTable: "programs",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_complaints_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "config_versions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entity_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_number = table.Column<int>(type: "integer", nullable: false),
                    snapshot = table.Column<string>(type: "jsonb", nullable: false),
                    change_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    change_summary = table.Column<string>(type: "text", nullable: true),
                    changed_by = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    changed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_config_versions", x => x.id);
                    table.ForeignKey(
                        name: "FK_config_versions_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "uq_tier_definitions_program_name",
                table: "tier_definitions",
                columns: new[] { "program_id", "name" },
                unique: true,
                filter: "status != 'deleted'");

            migrationBuilder.CreateIndex(
                name: "uq_tier_definitions_program_sort_order",
                table: "tier_definitions",
                columns: new[] { "program_id", "sort_order" },
                unique: true,
                filter: "status != 'deleted'");

            migrationBuilder.CreateIndex(
                name: "idx_complaints_tenant_program",
                table: "complaints",
                columns: new[] { "tenant_id", "program_id" });

            migrationBuilder.CreateIndex(
                name: "idx_complaints_tenant_status",
                table: "complaints",
                columns: new[] { "tenant_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_complaints_program_id",
                table: "complaints",
                column: "program_id");

            migrationBuilder.CreateIndex(
                name: "idx_config_versions_entity_timeline",
                table: "config_versions",
                columns: new[] { "tenant_id", "entity_type", "entity_id", "changed_at" });

            migrationBuilder.CreateIndex(
                name: "ux_config_versions_entity_version",
                table: "config_versions",
                columns: new[] { "tenant_id", "entity_type", "entity_id", "version_number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "complaints");

            migrationBuilder.DropTable(
                name: "config_versions");

            migrationBuilder.DropIndex(
                name: "uq_tier_definitions_program_name",
                table: "tier_definitions");

            migrationBuilder.DropIndex(
                name: "uq_tier_definitions_program_sort_order",
                table: "tier_definitions");

            migrationBuilder.DropColumn(
                name: "status",
                table: "tier_definitions");

            migrationBuilder.CreateIndex(
                name: "uq_tier_definitions_program_name",
                table: "tier_definitions",
                columns: new[] { "program_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_tier_definitions_program_sort_order",
                table: "tier_definitions",
                columns: new[] { "program_id", "sort_order" },
                unique: true);
        }
    }
}
