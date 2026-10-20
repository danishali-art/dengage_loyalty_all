namespace dEngage.Loyalty.Shared;

// Module and parameter names of the loyalty_config table (platform alignment D4). Same shape as
// the dEngage platform's sys.global_config (module_name, parameter_name), plus an optional
// tenant_id override. A missing row means the value in code applies, so every call site passes
// its current default and behaviour only changes when a row is added.
public static class LoyaltyConfigKeys
{
    public static class Api
    {
        public const string Module = "loyalty.api";

        // int, events per minute. Tenant override allowed. Code default: RateLimit:EventIngestionPerMinutePerTenant.
        public const string RateLimitEventIngestionPerMinute = "RateLimit.EventIngestionPerMinute";

        // string[] of field names masked in Customer 360's event data, on top of the built-in
        // list (CustomerPayloadMasker). Additive only: config can never un-mask a field. Tenant override allowed.
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
