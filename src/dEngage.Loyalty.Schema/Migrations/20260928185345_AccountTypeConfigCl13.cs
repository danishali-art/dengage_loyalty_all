using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace dEngage.Loyalty.Schema.Migrations
{
    /// <inheritdoc />
    public partial class AccountTypeConfigCl13 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_tier_qualifying",
                table: "account_types",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // 1.3.CL item 1 backfills. Trailing ";" on each statement is required by the
            // idempotent deploy script's DO $EF$ wrapper (see StackingResolutionCr06).
            //
            // Tier-qualifying flag: carry programs.qualifying_account_type_id over as-is so tier
            // evaluation keeps reading the same wallet. Restricted to the program's own account
            // types — a cross-program pointer was never valid and would otherwise mark another
            // program's wallet as qualifying.
            migrationBuilder.Sql("""
                UPDATE account_types at
                SET is_tier_qualifying = true
                FROM programs p
                WHERE p.qualifying_account_type_id = at.id
                  AND p.id = at.program_id;
                """);

            // Expiry warning: copy programs.warning_days into each POINTS wallet that expires,
            // only where it satisfies the job's own 0 < warning_days < expiration_days rule —
            // PointsExpiringDetectorJob already skipped every other combination, so nothing that
            // warned before stops warning. An existing config value is never overwritten.
            migrationBuilder.Sql("""
                UPDATE account_types at
                SET config = jsonb_set(at.config, '{warning_days}', to_jsonb(p.warning_days))
                FROM programs p
                WHERE p.id = at.program_id
                  AND at.type = 'POINTS'
                  AND p.warning_days IS NOT NULL
                  AND p.warning_days > 0
                  AND jsonb_typeof(at.config->'expiration_days') = 'number'
                  AND p.warning_days < (at.config->>'expiration_days')::numeric
                  AND NOT (at.config ? 'warning_days');
                """);

            // 1.3.CL item 3: CASH balances are real credit and never expire. No job ever read
            // this key for CASH (PointsExpirationJob is POINTS-only), so removing it has no
            // runtime effect beyond the customer 360 view no longer showing it.
            migrationBuilder.Sql("""
                UPDATE account_types
                SET config = config - 'expiration_days'
                WHERE type = 'CASH'
                  AND config ? 'expiration_days';
                """);

            migrationBuilder.CreateIndex(
                name: "ux_account_types_tier_qualifying",
                table: "account_types",
                columns: new[] { "tenant_id", "program_id" },
                unique: true,
                filter: "is_tier_qualifying");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // programs.warning_days / qualifying_account_type_id were never modified by Up, so
            // rolling back only has to drop what Up added. The stripped CASH expiration_days
            // values are not restored — no code path ever read them.
            migrationBuilder.Sql("""
                UPDATE account_types
                SET config = config - 'warning_days'
                WHERE config ? 'warning_days';
                """);

            migrationBuilder.DropIndex(
                name: "ux_account_types_tier_qualifying",
                table: "account_types");

            migrationBuilder.DropColumn(
                name: "is_tier_qualifying",
                table: "account_types");
        }
    }
}
