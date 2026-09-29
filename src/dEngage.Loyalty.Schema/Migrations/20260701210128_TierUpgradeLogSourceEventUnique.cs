using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace dEngage.Loyalty.Schema.Migrations
{
    /// <inheritdoc />
    public partial class TierUpgradeLogSourceEventUnique : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ux_tier_upgrade_log_source_event",
                table: "tier_upgrade_log",
                columns: new[] { "tenant_id", "contact_key", "source_event_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_tier_upgrade_log_source_event",
                table: "tier_upgrade_log");
        }
    }
}
