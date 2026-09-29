using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace dEngage.Loyalty.Schema.Migrations
{
    /// <inheritdoc />
    public partial class RewardTypeTaxonomy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "external_coupon_type",
                table: "reward_definitions");

            migrationBuilder.RenameColumn(
                name: "reward_type",
                table: "reward_log",
                newName: "reward_name");

            // Placeholder default for pre-existing rows only — 'points_bonus' is not a business
            // decision, it's a valid-registry placeholder so NOT NULL can be enforced immediately.
            // Per docs/scope-changes/2026-09-17-reward-type-taxonomy.md §5/§6: no safe RewardType
            // can be inferred per row without business input — audit and correct existing
            // reward_definitions rows before relying on this value for anything beyond "not null".
            migrationBuilder.AddColumn<string>(
                name: "reward_type",
                table: "reward_definitions",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "points_bonus");

            migrationBuilder.AddColumn<string>(
                name: "type_config",
                table: "reward_definitions",
                type: "jsonb",
                nullable: false,
                defaultValue: "{}");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "reward_type",
                table: "reward_definitions");

            migrationBuilder.DropColumn(
                name: "type_config",
                table: "reward_definitions");

            migrationBuilder.RenameColumn(
                name: "reward_name",
                table: "reward_log",
                newName: "reward_type");

            migrationBuilder.AddColumn<string>(
                name: "external_coupon_type",
                table: "reward_definitions",
                type: "character varying(255)",
                maxLength: 255,
                nullable: false,
                defaultValue: "");
        }
    }
}
