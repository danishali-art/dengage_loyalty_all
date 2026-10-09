using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace dEngage.Loyalty.Schema.Migrations
{
    /// <inheritdoc />
    public partial class ProgramDefaultRoundingCr1006 : Migration
    {
        // CR 2026-10-06 Phase 4 (docs/scope-changes/2026-10-05-rule-config-limits-by-trigger.md §3.8):
        // the rounding direction rules inherit. NOT NULL with default 'down' fills every existing
        // program with the rounding Spend rules already had, so existing payouts don't change.

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "default_rounding",
                table: "programs",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "down");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "default_rounding",
                table: "programs");
        }
    }
}
