using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace dEngage.Loyalty.Schema.Migrations
{
    /// <summary>
    /// Platform alignment D4, decided 2026-10-20: tenant settings (the ingestion rate limit and the
    /// extra masked fields) are kept per tenant in loyalty_tenant_config with no global value.
    /// Order matters: create the table, give every existing tenant its rows (carrying over the
    /// current global value), remove the two global rows, and only then forbid them in loyalty_config.
    /// New tenants get their rows when they are created (PlatformAppService).
    /// </summary>
    public partial class LoyaltyTenantConfigAlignD4 : Migration
    {
        private const string MigratedBy = "migration:20261020150844_LoyaltyTenantConfigAlignD4";

        private const string IsTenantSetting =
            "(module_name = 'loyalty.api' AND parameter_name = 'RateLimit.EventIngestionPerMinute') OR " +
            "(module_name = 'loyalty.api' AND parameter_name = 'Pii.ExtraMaskedFields')";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "loyalty_tenant_config",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    module_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    parameter_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    value = table.Column<string>(type: "text", nullable: true),
                    value_json = table.Column<string>(type: "jsonb", nullable: true),
                    is_secret = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_by = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_loyalty_tenant_config", x => x.id);
                    table.CheckConstraint("ck_loyalty_tenant_config_tenant_settings", IsTenantSetting);
                    table.ForeignKey(
                        name: "FK_loyalty_tenant_config_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ux_loyalty_tenant_config_key",
                table: "loyalty_tenant_config",
                columns: new[] { "tenant_id", "module_name", "parameter_name" },
                unique: true);

            // Every existing tenant gets a row per tenant setting: the environment's current global
            // value when there is one (so nothing changes for anybody), otherwise the starting value
            // (frozen copy of LoyaltyConfigDefaults.NewTenant).
            migrationBuilder.Sql($"""
                INSERT INTO loyalty_tenant_config (id, tenant_id, module_name, parameter_name, value, value_json, is_secret, description, updated_at, updated_by)
                SELECT gen_random_uuid(), t.id, k.module_name, k.parameter_name,
                       CASE WHEN g.id IS NULL THEN k.value ELSE g.value END,
                       CASE WHEN g.id IS NULL THEN k.value_json ELSE g.value_json END,
                       COALESCE(g.is_secret, false), k.description, now(), '{MigratedBy}'
                FROM tenants t
                CROSS JOIN (VALUES
                    ('loyalty.api', 'RateLimit.EventIngestionPerMinute', '120', NULL::jsonb,
                     'Events accepted per minute for this tenant.'),
                    ('loyalty.api', 'Pii.ExtraMaskedFields', NULL, '[]'::jsonb,
                     'Extra field names masked in Customer 360 event data for this tenant, on top of the built-in list (additive).')
                ) AS k(module_name, parameter_name, value, value_json, description)
                LEFT JOIN loyalty_config g ON g.module_name = k.module_name AND g.parameter_name = k.parameter_name
                ON CONFLICT (tenant_id, module_name, parameter_name) DO NOTHING;
                """);

            // Tenant settings have no global value any more.
            migrationBuilder.Sql($"DELETE FROM loyalty_config WHERE {IsTenantSetting};");

            migrationBuilder.AddCheckConstraint(
                name: "ck_loyalty_config_global_settings",
                table: "loyalty_config",
                sql: $"NOT ({IsTenantSetting})");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_loyalty_config_global_settings",
                table: "loyalty_config");

            // Back to one global value per setting. Per-tenant values can't be merged into one, so
            // the starting values come back — exactly as the migrations that first seeded them wrote
            // them, so rolling those back as well removes them cleanly.
            migrationBuilder.Sql("""
                INSERT INTO loyalty_config (module_name, parameter_name, value, value_json, is_secret, description, updated_at, updated_by)
                VALUES
                    ('loyalty.api', 'RateLimit.EventIngestionPerMinute', '120', NULL, false,
                     'Events accepted per minute per tenant (the same limit applies to every tenant).', now(),
                     'migration:20261020143545_LoyaltyConfigGlobalOnlyAlignD4'),
                    ('loyalty.api', 'Pii.ExtraMaskedFields', NULL, '[]'::jsonb, false,
                     'Extra field names masked in Customer 360 event data, on top of the built-in list (additive). Usually set per tenant.', now(),
                     'migration:20261020142338_LoyaltyConfigDefaultsAlignD4')
                ON CONFLICT (module_name, parameter_name) DO NOTHING;
                """);

            migrationBuilder.DropTable(
                name: "loyalty_tenant_config");
        }
    }
}
