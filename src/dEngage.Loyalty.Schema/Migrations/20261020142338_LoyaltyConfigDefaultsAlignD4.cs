using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace dEngage.Loyalty.Schema.Migrations
{
    /// <summary>
    /// Platform alignment D4: global rows (tenant_id NULL) for the settings read from loyalty_config,
    /// with the values the code used before, so every setting is visible and editable in the table.
    /// None is required: without a row the same default applies in code.
    /// RateLimit.EventIngestionPerMinute is deliberately not seeded: its default comes from each
    /// environment's appsettings (600 / 300 / 120), which one global row would override everywhere.
    /// ON CONFLICT keeps a value an environment already set.
    /// </summary>
    public partial class LoyaltyConfigDefaultsAlignD4 : Migration
    {
        private const string SeededBy = "migration:20261020142338_LoyaltyConfigDefaultsAlignD4";

        // (module, parameter, value, value_json, description)
        private static readonly (string Module, string Parameter, string Value, string ValueJson, string Description)[] Defaults =
        [
            ("loyalty.api", "Pii.ExtraMaskedFields", null, "[]",
                "Extra field names masked in Customer 360 event data, on top of the built-in list (additive). Usually set per tenant."),
            ("loyalty.api", "Customer360.MessageWindowBeforeMinutes", "1", null,
                "Minutes before an event in which its sent messages are looked up (Customer 360)."),
            ("loyalty.api", "Customer360.MessageWindowAfterMinutes", "60", null,
                "Minutes after an event in which its sent messages are looked up (Customer 360)."),
            ("loyalty.engine", "RuleCache.TtlMinutes", "60", null,
                "Redis safety-net TTL of the rule cache, in minutes. Freshness comes from RuleSync."),
            ("loyalty.engine", "CampaignConfigCache.TtlMinutes", "60", null,
                "Redis safety-net TTL of the campaign config cache, in minutes."),
            ("loyalty.jobs", "Outbox.BatchSize", "100", null,
                "Outbox rows published per batch."),
            ("loyalty.jobs", "Outbox.MaxAttempts", "8", null,
                "Publish attempts before an outbox row is marked failed."),
            ("loyalty.jobs", "Outbox.BackoffMinutes", null, "[1, 5, 30, 120, 720]",
                "Minutes to wait before each retry of an outbox row; the last value repeats."),
            ("loyalty.jobs", "DelayedPosting.BatchSize", "500", null,
                "Held postings promoted per hourly run; the next run picks up the rest."),
            ("loyalty.jobs", "EventLogRetention.BatchSize", "10000", null,
                "event_log rows deleted per batch by the nightly retention job."),
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var d in Defaults)
            {
                migrationBuilder.Sql($"""
                    INSERT INTO loyalty_config (module_name, parameter_name, tenant_id, value, value_json, is_secret, description, updated_at, updated_by)
                    VALUES ({Literal(d.Module)}, {Literal(d.Parameter)}, NULL, {Literal(d.Value)}, {Json(d.ValueJson)}, false, {Literal(d.Description)}, now(), {Literal(SeededBy)})
                    ON CONFLICT (module_name, parameter_name) WHERE tenant_id IS NULL DO NOTHING;
                    """);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Only rows still exactly as seeded; a value someone changed is kept.
            foreach (var d in Defaults)
            {
                migrationBuilder.Sql($"""
                    DELETE FROM loyalty_config
                    WHERE tenant_id IS NULL AND module_name = {Literal(d.Module)} AND parameter_name = {Literal(d.Parameter)}
                      AND updated_by = {Literal(SeededBy)}
                      AND value IS NOT DISTINCT FROM {Literal(d.Value)}
                      AND value_json IS NOT DISTINCT FROM {Json(d.ValueJson)};
                    """);
            }
        }

        private static string Literal(string value) => value is null ? "NULL" : "'" + value.Replace("'", "''") + "'";

        private static string Json(string value) => value is null ? "NULL" : Literal(value) + "::jsonb";
    }
}
