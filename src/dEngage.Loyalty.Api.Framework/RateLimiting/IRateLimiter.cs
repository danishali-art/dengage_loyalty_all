namespace dEngage.Loyalty.Api.Framework.RateLimiting;

public interface IRateLimiter
{
    // Fixed-window token bucket keyed by (bucket, tenantId) — good enough for per-tenant ingestion
    // throttling without the bookkeeping of a true sliding window.
    Task<bool> TryAcquireAsync(string bucket, string tenantId, int limitPerMinute, CancellationToken ct);
}
