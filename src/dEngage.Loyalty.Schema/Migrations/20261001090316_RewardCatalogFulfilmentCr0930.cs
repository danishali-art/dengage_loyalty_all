using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace dEngage.Loyalty.Schema.Migrations
{
    /// <inheritdoc />
    public partial class RewardCatalogFulfilmentCr0930 : Migration
    {
        // CR 2026-09-30 (docs/scope-changes/2026-09-30-reward-acquisition-fulfilment.md), P2-P4.
        // Run the pre-deploy queries in that document (§6) first: they list the rewards this
        // deactivates, the proposed program slugs, and any active reward names that would block
        // ux_reward_definitions_tenant_name_active.

        // A1 + O2 + the §3.1 matrix: everything a reward can no longer be. Rows are deactivated,
        // never deleted — RewardLog history keeps pointing at them.
        private const string RetiredRewards = """
            is_active
            AND (acquisition NOT IN ('points_purchase', 'streak_completion')
              OR reward_type NOT IN ('cashback', 'tier_upgrade')
              OR (reward_type = 'tier_upgrade' AND acquisition <> 'streak_completion')
              OR (reward_type = 'cashback' AND NOT (type_config ? 'cash_account_type_id')))
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // A4: approval columns. The default backfills existing rows as approved — none of them
            // ever needed an approval before.
            migrationBuilder.AddColumn<string>(
                name: "approved_by",
                table: "reward_definitions",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "created_by",
                table: "reward_definitions",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "status",
                table: "reward_definitions",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "active");

            // §3.6: tier lock for reward-driven upgrades.
            migrationBuilder.AddColumn<DateOnly>(
                name: "tier_locked_until",
                table: "customer_accounts",
                type: "date",
                nullable: true);

            // A5: program slug — add nullable, backfill, then tighten (backend-schema-shared.md).
            migrationBuilder.AddColumn<string>(
                name: "slug",
                table: "programs",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            // Backfill from the name: lowercase, every non [a-z0-9] run -> '-', trimmed, at most 32
            // chars, at least 2 (else prefixed 'program-'). No '_' can survive, which is what keeps
            // {slug}_{suffix} reward names unambiguous. The first program (oldest) with a given slug
            // in a tenant keeps it; later ones get a short piece of their own id appended.
            // Trailing ";" required by the idempotent deploy script's DO $EF$ wrapper.
            migrationBuilder.Sql("""
                WITH base AS (
                    SELECT id, tenant_id, created_at,
                           trim(both '-' from left(trim(both '-' from regexp_replace(lower(name), '[^a-z0-9]+', '-', 'g')), 32)) AS s
                    FROM programs
                ),
                fixed AS (
                    SELECT id, tenant_id, created_at,
                           CASE WHEN length(s) < 2 THEN 'program-' || s ELSE s END AS s
                    FROM base
                ),
                numbered AS (
                    SELECT id, s, row_number() OVER (PARTITION BY tenant_id, s ORDER BY created_at, id) AS n
                    FROM fixed
                )
                UPDATE programs p
                SET slug = CASE WHEN n.n = 1 THEN trim(both '-' from n.s)
                                ELSE trim(both '-' from n.s) || '-' || left(replace(p.id::text, '-', ''), 6) END
                FROM numbered n
                WHERE p.id = n.id;
                """);

            migrationBuilder.AlterColumn<string>(
                name: "slug",
                table: "programs",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40,
                oldNullable: true);

            // DB-only safety net, deliberately not in the EF model: raw-SQL inserts that predate the
            // column (the tenant seed scripts in scripts/*.sql, which are reference data and stay
            // untouched) still get a valid, unique slug. Code paths always set it explicitly.
            migrationBuilder.Sql("""
                ALTER TABLE programs ALTER COLUMN slug SET DEFAULT ('p-' || left(replace(gen_random_uuid()::text, '-', ''), 10));
                """);

            migrationBuilder.CreateIndex(
                name: "ux_programs_tenant_slug",
                table: "programs",
                columns: new[] { "tenant_id", "slug" },
                unique: true);

            // A1: deactivate retired rewards. Same effect as IProgramChangeTracker.MarkChangedAsync
            // for an API edit — runs first, while the affected rows still match.
            migrationBuilder.Sql($"""
                UPDATE programs
                SET has_unpublished_changes = true
                WHERE publication_status = 'published'
                  AND id IN (SELECT program_id FROM reward_definitions WHERE {RetiredRewards});
                """);

            migrationBuilder.Sql($"""
                UPDATE reward_definitions
                SET is_active = false
                WHERE {RetiredRewards};
                """);

            // Created after the deactivation above, so only the rewards that stay active must be
            // unique by name per tenant. Fails loudly if two still-active rewards share a name
            // across programs — resolve those first (pre-deploy query, CR §6).
            migrationBuilder.CreateIndex(
                name: "ux_reward_definitions_tenant_name_active",
                table: "reward_definitions",
                columns: new[] { "tenant_id", "name" },
                unique: true,
                filter: "is_active");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Schema only. Rewards deactivated in Up stay deactivated (which ones were active before
            // isn't recorded); re-activating one is an admin decision, not a schema step.
            migrationBuilder.DropIndex(
                name: "ux_reward_definitions_tenant_name_active",
                table: "reward_definitions");

            migrationBuilder.DropIndex(
                name: "ux_programs_tenant_slug",
                table: "programs");

            migrationBuilder.DropColumn(
                name: "approved_by",
                table: "reward_definitions");

            migrationBuilder.DropColumn(
                name: "created_by",
                table: "reward_definitions");

            migrationBuilder.DropColumn(
                name: "status",
                table: "reward_definitions");

            migrationBuilder.DropColumn(
                name: "slug",
                table: "programs");

            migrationBuilder.DropColumn(
                name: "tier_locked_until",
                table: "customer_accounts");
        }
    }
}
