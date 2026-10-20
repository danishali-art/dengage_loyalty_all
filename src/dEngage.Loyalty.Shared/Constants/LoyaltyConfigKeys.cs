namespace dEngage.Loyalty.Shared;

// Module and parameter names of the loyalty settings (platform alignment D4). Same shape as the
// dEngage platform's sys.global_config (module_name, parameter_name). Two kinds, never mixed:
//   - global settings: one row in loyalty_config, the same for the whole deployment;
//   - tenant settings (TenantSettings below): one row per tenant in loyalty_tenant_config, and
//     no global value at all — like the platform's sys.config_account_* tables.
// The tables are the only source; a missing row fails. Starting values and descriptions:
// Schema/LoyaltyConfigDefaults.cs.
public static class LoyaltyConfigKeys
{
    // The settings that are kept per tenant. Both tables enforce the split with CHECK constraints
    // generated from this list, so changing it needs a new migration (plus a backfill for existing
    // tenants when a key is added).
    public static readonly IReadOnlyList<(string Module, string Parameter)> TenantSettings =
    [
        (Api.Module, Api.RateLimitEventIngestionPerMinute),
        (Api.Module, Api.PiiExtraMaskedFields),
    ];

    public static bool IsTenantSetting(string module, string parameter) =>
        TenantSettings.Contains((module, parameter));

    public static class Api
    {
        public const string Module = "loyalty.api";

        // int, events accepted per minute. Tenant setting.
        public const string RateLimitEventIngestionPerMinute = "RateLimit.EventIngestionPerMinute";

        // string[] of field names masked in Customer 360's event data, on top of the built-in
        // list (CustomerPayloadMasker). Additive only: config can never un-mask a field. Tenant setting.
        public const string PiiExtraMaskedFields = "Pii.ExtraMaskedFields";

        // int, minutes. Window around an event in which its sent messages are looked up (Customer 360).
        public const string Customer360MessageWindowBeforeMinutes = "Customer360.MessageWindowBeforeMinutes";
        public const string Customer360MessageWindowAfterMinutes = "Customer360.MessageWindowAfterMinutes";
    }

    public static class Engine
    {
        public const string Module = "loyalty.engine";

        // int, minutes. Redis safety-net TTL of the rule and campaign caches.
        public const string RuleCacheTtlMinutes = "RuleCache.TtlMinutes";
        public const string CampaignConfigCacheTtlMinutes = "CampaignConfigCache.TtlMinutes";
    }

    public static class Jobs
    {
        public const string Module = "loyalty.jobs";

        // Outbox publisher: int, int, int[] (minutes between attempts; the last value repeats).
        public const string OutboxBatchSize = "Outbox.BatchSize";
        public const string OutboxMaxAttempts = "Outbox.MaxAttempts";
        public const string OutboxBackoffMinutes = "Outbox.BackoffMinutes";

        // int, rows per run / per delete batch.
        public const string DelayedPostingBatchSize = "DelayedPosting.BatchSize";
        public const string EventLogRetentionBatchSize = "EventLogRetention.BatchSize";
    }
}
