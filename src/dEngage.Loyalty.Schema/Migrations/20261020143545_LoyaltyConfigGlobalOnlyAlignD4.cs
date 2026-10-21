using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace dEngage.Loyalty.Schema.Migrations
{
    /// <summary>
    /// Platform alignment D4, decided 2026-10-20: loyalty_config holds global settings only and is the
    /// only source (no tenant overrides, no defaults in code or appsettings). Drops tenant_id with
    /// its indexes and seeds the one setting that wasn't seeded before, the ingestion rate limit.
    /// </summary>
    public partial class LoyaltyConfigGlobalOnlyAlignD4 : Migration
    {
        private const string SeededBy = "migration:20261020143545_LoyaltyConfigGlobalOnlyAlignD4";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Tenant overrides are discarded by decision (no environment had any when this was
            // written). Removing them first also keeps the new unique index from failing on a
            // tenant row that has the same key as a global row.
            migrationBuilder.Sql("DELETE FROM loyalty_config WHERE tenant_id IS NOT NULL;");

            migrationBuilder.DropForeignKey(
                name: "FK_loyalty_config_tenants_tenant_id",
                table: "loyalty_config");

            migrationBuilder.DropIndex(
                name: "ux_loyalty_config_global",
                table: "loyalty_config");

            migrationBuilder.DropIndex(
                name: "ux_loyalty_config_tenant",
                table: "loyalty_config");

            migrationBuilder.DropColumn(
                name: "tenant_id",
                table: "loyalty_config");

            migrationBuilder.CreateIndex(
                name: "ux_loyalty_config_key",
                table: "loyalty_config",
                columns: new[] { "module_name", "parameter_name" },
                unique: true);

            // 120 = the previous production value (appsettings RateLimit, now removed). Environments
            // that need more raise the row. ON CONFLICT keeps a value an environment already set.
            migrationBuilder.Sql($"""
                INSERT INTO loyalty_config (module_name, parameter_name, value, value_json, is_secret, description, updated_at, updated_by)
                VALUES ('loyalty.api', 'RateLimit.EventIngestionPerMinute', '120', NULL, false,
                        'Events accepted per minute per tenant (the same limit applies to every tenant).', now(), '{SeededBy}')
                ON CONFLICT (module_name, parameter_name) DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Only if still exactly as seeded; a value someone changed is kept.
            migrationBuilder.Sql($"""
                DELETE FROM loyalty_config
                WHERE module_name = 'loyalty.api' AND parameter_name = 'RateLimit.EventIngestionPerMinute'
                  AND value = '120' AND updated_by = '{SeededBy}';
                """);

            migrationBuilder.DropIndex(
                name: "ux_loyalty_config_key",
                table: "loyalty_config");

            migrationBuilder.AddColumn<Guid>(
                name: "tenant_id",
                table: "loyalty_config",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ux_loyalty_config_global",
                table: "loyalty_config",
                columns: new[] { "module_name", "parameter_name" },
                unique: true,
                filter: "tenant_id IS NULL");

            migrationBuilder.CreateIndex(
                name: "ux_loyalty_config_tenant",
                table: "loyalty_config",
                columns: new[] { "tenant_id", "module_name", "parameter_name" },
                unique: true,
                filter: "tenant_id IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_loyalty_config_tenants_tenant_id",
                table: "loyalty_config",
                column: "tenant_id",
                principalTable: "tenants",
                principalColumn: "id");
        }
    }
}
