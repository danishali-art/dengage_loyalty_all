using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace dEngage.Loyalty.Schema.Migrations
{
    /// <inheritdoc />
    public partial class RuleStackingRetirementCl13 : Migration
    {
        // 1.3.CL item 5: named exclusivity groups and multiplier stacking are retired. Data only —
        // the columns stay (nullable group, stack_mode defaulting to 'Additive').
        //
        // Clearing them changes how a rule resolves, so it is treated exactly like an edit
        // (CR-09 contract, RuleVersioningService.ArchiveAndBumpAsync): archive the pre-change
        // state as the rule's current version, then bump current_version. Postings made after
        // this migration reference a version whose snapshot matches the rule's real behaviour.
        // updated_at is touched so RuleSyncService's 30s delta reloads the Redis rule cache.
        //
        // Rules whose group is still the CR-06 backfill value (the account type name) resolve
        // exactly as before — WinnerSelector falls back to grouping by target account type.
        // Only custom-named groups and Multiplier rules change behaviour; list them with the
        // pre-deploy query in docs/scope-changes/2026-09-28-program-account-type-changes.md.
        private const string Affected = "(exclusivity_group IS NOT NULL OR stack_mode <> 'Additive')";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Trailing ";" required by the idempotent deploy script's DO $EF$ wrapper.
            migrationBuilder.Sql($"""
                INSERT INTO rule_versions (
                    id, tenant_id, rule_id, version_number, name, type, trigger, conditions,
                    calculation, target_account_type_id, limits, configuration, priority,
                    stackable, exclusivity_group, stack_mode, active_from, active_to,
                    effective_from, created_at)
                SELECT
                    gen_random_uuid(), r.tenant_id, r.id, r.current_version, r.name, r.type, r.trigger, r.conditions,
                    r.calculation, r.target_account_type_id, r.limits, r.configuration, r.priority,
                    r.stackable, r.exclusivity_group, r.stack_mode, r.active_from, r.active_to,
                    now(), now()
                FROM rules r
                WHERE {Affected};
                """);

            migrationBuilder.Sql($"""
                UPDATE rules
                SET exclusivity_group = NULL,
                    stack_mode        = 'Additive',
                    current_version   = current_version + 1,
                    updated_at        = now()
                WHERE {Affected};
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Not reversible by design: the archived rule_versions rows hold the previous
            // groups/modes, and rules are never rolled back in place (CR-09 — restore by a new
            // edit instead). Intentionally a no-op so a schema rollback past this point works.
        }
    }
}
