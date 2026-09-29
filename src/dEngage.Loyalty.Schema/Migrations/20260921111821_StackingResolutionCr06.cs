using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace dEngage.Loyalty.Schema.Migrations
{
    /// <inheritdoc />
    public partial class StackingResolutionCr06 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "exclusivity_group",
                table: "rules",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "stack_mode",
                table: "rules",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Additive");

            migrationBuilder.AddColumn<string>(
                name: "resolution_snapshot",
                table: "rule_fire_audit",
                type: "jsonb",
                nullable: true);

            // Part B migration note #3: backfill exclusivity_group on existing non-stackable
            // rules with their target account's name, so every pre-existing exclusive rule
            // keeps competing only against rules that already shared the same wallet —
            // preserving current behavior exactly (CR-06's A6 default: "exclusivity group
            // defaults to the target account name"). ReversalRule rows (target_account_type_id
            // IS NULL) are untouched — always Stackable per WinnerSelector's own bypass, but
            // left null here defensively since this migration cannot know that invariant holds
            // for every historical row.
            // Trailing ";" is required, not cosmetic: the idempotent deploy script
            // (`dotnet ef migrations script --idempotent`, see scripts/loyalty_schema.sql) wraps
            // this in a `DO $EF$ BEGIN IF NOT EXISTS(...) THEN <sql> END IF; END $EF$;` block,
            // where a missing terminator before `END IF;` is a PL/pgSQL syntax error.
            migrationBuilder.Sql("""
                UPDATE rules
                SET exclusivity_group = account_types.name
                FROM account_types
                WHERE rules.target_account_type_id = account_types.id
                  AND rules.stackable = false
                  AND rules.exclusivity_group IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "exclusivity_group",
                table: "rules");

            migrationBuilder.DropColumn(
                name: "stack_mode",
                table: "rules");

            migrationBuilder.DropColumn(
                name: "resolution_snapshot",
                table: "rule_fire_audit");
        }
    }
}
