using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace dEngage.Loyalty.Schema.Migrations
{
    /// <inheritdoc />
    public partial class HeldPostingRefundsCr1006 : Migration
    {
        // CR 2026-10-06 H1 (docs/scope-changes/2026-10-05-rule-config-limits-by-trigger.md §3.4):
        // a refund during the hold takes the held points back instead of failing. Additive only —
        // a nullable cancelled_at on held_postings and the append-only held_posting_refunds table;
        // no existing row changes.

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "cancelled_at",
                table: "held_postings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "held_posting_refunds",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    held_posting_id = table.Column<Guid>(type: "uuid", nullable: false),
                    refund_event_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    delta = table.Column<decimal>(type: "numeric(20,4)", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_held_posting_refunds", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "idx_held_postings_tenant_source_event",
                table: "held_postings",
                columns: new[] { "tenant_id", "source_event_id" });

            migrationBuilder.CreateIndex(
                name: "ux_held_posting_refunds_tenant_held_refund",
                table: "held_posting_refunds",
                columns: new[] { "tenant_id", "held_posting_id", "refund_event_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "held_posting_refunds");

            migrationBuilder.DropIndex(
                name: "idx_held_postings_tenant_source_event",
                table: "held_postings");

            migrationBuilder.DropColumn(
                name: "cancelled_at",
                table: "held_postings");
        }
    }
}
