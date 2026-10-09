using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace dEngage.Loyalty.Schema.Migrations
{
    /// <inheritdoc />
    public partial class LedgerExpiresAtCr1006 : Migration
    {
        // CR 2026-10-06 Phase 5 (docs/scope-changes/2026-10-05-rule-config-limits-by-trigger.md §3.9):
        // the expiry date of an earn / transfer_in entry, set once on insert. Additive and nullable;
        // ADD COLUMN on the partitioned parent reaches every tenant partition. No backfill — ledger
        // rows are never updated; a null means "earn date + the wallet's expiration_days", exactly
        // today's behaviour.

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "expires_at",
                table: "ledger_entries",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "expires_at",
                table: "ledger_entries");
        }
    }
}
