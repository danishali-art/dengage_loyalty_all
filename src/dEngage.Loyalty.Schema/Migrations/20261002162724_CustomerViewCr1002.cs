using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace dEngage.Loyalty.Schema.Migrations
{
    /// <inheritdoc />
    public partial class CustomerViewCr1002 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "contact_key",
                table: "event_inbox",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "idx_streak_progress_tenant_contact",
                table: "streak_progress",
                columns: new[] { "tenant_id", "contact_key" });

            migrationBuilder.CreateIndex(
                name: "idx_streak_log_tenant_contact",
                table: "streak_log",
                columns: new[] { "tenant_id", "contact_key" });

            migrationBuilder.CreateIndex(
                name: "idx_rule_fire_audit_tenant_contact_date",
                table: "rule_fire_audit",
                columns: new[] { "tenant_id", "contact_key", "created_at" });

            migrationBuilder.CreateIndex(
                name: "idx_outbox_tenant_contact_date",
                table: "outbox_events",
                columns: new[] { "tenant_id", "contact_key", "created_at" });

            migrationBuilder.CreateIndex(
                name: "idx_held_postings_tenant_contact_date",
                table: "held_postings",
                columns: new[] { "tenant_id", "contact_key", "created_at" });

            migrationBuilder.CreateIndex(
                name: "idx_event_inbox_tenant_contact_received",
                table: "event_inbox",
                columns: new[] { "tenant_id", "contact_key", "received_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "idx_streak_progress_tenant_contact",
                table: "streak_progress");

            migrationBuilder.DropIndex(
                name: "idx_streak_log_tenant_contact",
                table: "streak_log");

            migrationBuilder.DropIndex(
                name: "idx_rule_fire_audit_tenant_contact_date",
                table: "rule_fire_audit");

            migrationBuilder.DropIndex(
                name: "idx_outbox_tenant_contact_date",
                table: "outbox_events");

            migrationBuilder.DropIndex(
                name: "idx_held_postings_tenant_contact_date",
                table: "held_postings");

            migrationBuilder.DropIndex(
                name: "idx_event_inbox_tenant_contact_received",
                table: "event_inbox");

            migrationBuilder.DropColumn(
                name: "contact_key",
                table: "event_inbox");
        }
    }
}
