using System.Collections.Concurrent;
using dEngage.Loyalty.Api.Framework.RateLimiting;

namespace dEngage.Loyalty.IntegrationTests.Fixtures;

// Always grants — stands in for RedisRateLimiter so the test host needs no real Redis.
// Records each request's limit so tests can check which limit the Api applied (alignment D4).
public sealed class FakeRateLimiter : IRateLimiter
{
    public ConcurrentQueue<(string Bucket, string TenantId, int LimitPerMinute)> Requests { get; } = new();

    public Task<bool> TryAcquireAsync(string bucket, string tenantId, int limitPerMinute, CancellationToken ct)
    {
        Requests.Enqueue((bucket, tenantId, limitPerMinute));
        return Task.FromResult(true);
    }
}
