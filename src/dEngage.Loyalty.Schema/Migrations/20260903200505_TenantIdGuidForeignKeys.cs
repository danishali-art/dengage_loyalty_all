using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace dEngage.Loyalty.Schema.Migrations
{
    /// <inheritdoc />
    public partial class TenantIdGuidForeignKeys : Migration
    {
        // Guid-Id PK, tenant_id is an ordinary (non-key) column — no PK juggling needed.
        private static readonly string[] SimpleTables =
        {
            "account_types", "customer_accounts", "outbox_events", "programs",
            "reward_definitions", "reward_log", "rules", "rule_fire_audit",
            "streak_campaigns", "streak_log", "tier_definitions", "tier_upgrade_log"
        };

        // tenant_id is part of the composite primary key — the PK constraint must be dropped
        // before the column can be dropped, and recreated afterward on the same columns.
        private static readonly (string Table, string[] KeyColumns)[] CompositeKeyTables =
        {
            ("streak_applied_event", new[] { "tenant_id", "campaign_id", "event_id" }),
            ("streak_period_state", new[] { "tenant_id", "campaign_id", "contact_key", "period_start" }),
            ("streak_progress", new[] { "tenant_id", "campaign_id", "contact_key" }),
        };

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var table in SimpleTables)
            {
                migrationBuilder.Sql($"""
                    ALTER TABLE {table} ADD COLUMN tenant_id_new uuid;
                    UPDATE {table} t SET tenant_id_new = tn.id FROM tenants tn WHERE tn.slug = t.tenant_id;
                    ALTER TABLE {table} ALTER COLUMN tenant_id_new SET NOT NULL;
                    ALTER TABLE {table} DROP COLUMN tenant_id;
                    ALTER TABLE {table} RENAME COLUMN tenant_id_new TO tenant_id;
                    ALTER TABLE {table} ADD CONSTRAINT "FK_{table}_tenants_tenant_id"
                        FOREIGN KEY (tenant_id) REFERENCES tenants(id) ON DELETE CASCADE;
                    """);
            }

            foreach (var (table, keyColumns) in CompositeKeyTables)
            {
                var keyList = string.Join(", ", keyColumns);
                migrationBuilder.Sql($"""
                    ALTER TABLE {table} DROP CONSTRAINT "PK_{table}";
                    ALTER TABLE {table} ADD COLUMN tenant_id_new uuid;
                    UPDATE {table} t SET tenant_id_new = tn.id FROM tenants tn WHERE tn.slug = t.tenant_id;
                    ALTER TABLE {table} ALTER COLUMN tenant_id_new SET NOT NULL;
                    ALTER TABLE {table} DROP COLUMN tenant_id;
                    ALTER TABLE {table} RENAME COLUMN tenant_id_new TO tenant_id;
                    ALTER TABLE {table} ADD CONSTRAINT "PK_{table}" PRIMARY KEY ({keyList});
                    ALTER TABLE {table} ADD CONSTRAINT "FK_{table}_tenants_tenant_id"
                        FOREIGN KEY (tenant_id) REFERENCES tenants(id) ON DELETE CASCADE;
                    """);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Structural rollback only — recovers the original slug values via the same join,
            // in reverse (works as long as no tenant row was deleted between Up() and Down()).
            foreach (var table in SimpleTables)
            {
                migrationBuilder.Sql($"""
                    ALTER TABLE {table} DROP CONSTRAINT "FK_{table}_tenants_tenant_id";
                    ALTER TABLE {table} ADD COLUMN tenant_id_old character varying(100);
                    UPDATE {table} t SET tenant_id_old = tn.slug FROM tenants tn WHERE tn.id = t.tenant_id;
                    ALTER TABLE {table} ALTER COLUMN tenant_id_old SET NOT NULL;
                    ALTER TABLE {table} DROP COLUMN tenant_id;
                    ALTER TABLE {table} RENAME COLUMN tenant_id_old TO tenant_id;
                    """);
            }

            foreach (var (table, keyColumns) in CompositeKeyTables)
            {
                var keyList = string.Join(", ", keyColumns);
                migrationBuilder.Sql($"""
                    ALTER TABLE {table} DROP CONSTRAINT "FK_{table}_tenants_tenant_id";
                    ALTER TABLE {table} DROP CONSTRAINT "PK_{table}";
                    ALTER TABLE {table} ADD COLUMN tenant_id_old character varying(100);
                    UPDATE {table} t SET tenant_id_old = tn.slug FROM tenants tn WHERE tn.id = t.tenant_id;
                    ALTER TABLE {table} ALTER COLUMN tenant_id_old SET NOT NULL;
                    ALTER TABLE {table} DROP COLUMN tenant_id;
                    ALTER TABLE {table} RENAME COLUMN tenant_id_old TO tenant_id;
                    ALTER TABLE {table} ADD CONSTRAINT "PK_{table}" PRIMARY KEY ({keyList});
                    """);
            }
        }
    }
}
