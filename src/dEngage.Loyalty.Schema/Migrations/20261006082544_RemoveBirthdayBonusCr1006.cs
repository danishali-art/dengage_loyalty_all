using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace dEngage.Loyalty.Schema.Migrations
{
    /// <inheritdoc />
    public partial class RemoveBirthdayBonusCr1006 : Migration
    {
        // CR 2026-10-05 addendum A (A-D2, A-D4): the birthday bonus is removed end to end. Rules and
        // streak campaigns on the retired birthdaybonus trigger are disabled, never deleted (rule
        // versions untouched); updated_at is touched so RuleSyncService reloads the caches. Ledger
        // postings the job already made (source event id birthday:{contactKey}:{year}) are not
        // touched. customer_birthdays is dropped without an export by decision.
        private const string AffectedRules = """
            status IN ('active', 'pending_approval') AND trigger = 'birthdaybonus'
            """;

        private const string AffectedCampaigns = """
            status = 'active' AND trigger = 'birthdaybonus'
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Same effect as IProgramChangeTracker.MarkChangedAsync; runs first, while the
            // affected rows still match. Trailing ";" required by the idempotent deploy script's
            // DO $EF$ wrapper.
            migrationBuilder.Sql($"""
                UPDATE programs
                SET has_unpublished_changes = true
                WHERE publication_status = 'published'
                  AND (id IN (SELECT program_id FROM rules WHERE {AffectedRules})
                    OR id IN (SELECT program_id FROM streak_campaigns WHERE {AffectedCampaigns}));
                """);

            migrationBuilder.Sql($"""
                UPDATE rules
                SET status     = 'disabled',
                    updated_at = now()
                WHERE {AffectedRules};
                """);

            migrationBuilder.Sql($"""
                UPDATE streak_campaigns
                SET status     = 'disabled',
                    updated_at = now()
                WHERE {AffectedCampaigns};
                """);

            migrationBuilder.DropTable(
                name: "customer_birthdays");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Recreates the empty table and its indexes only; dropped birthdays and disabled
            // rules/campaigns are not restored (same as RetireStampsAndExpiryRuleCr1005).
            migrationBuilder.CreateTable(
                name: "customer_birthdays",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    contact_key = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    month_day = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customer_birthdays", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "idx_customer_birthdays_month_day",
                table: "customer_birthdays",
                columns: new[] { "tenant_id", "month_day" });

            migrationBuilder.CreateIndex(
                name: "ux_customer_birthdays_tenant_contact",
                table: "customer_birthdays",
                columns: new[] { "tenant_id", "contact_key" },
                unique: true);
        }
    }
}
