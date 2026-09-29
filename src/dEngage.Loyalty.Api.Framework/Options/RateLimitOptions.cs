namespace dEngage.Loyalty.Api.Framework.Options;

public sealed class RateLimitOptions
{
    public int EventIngestionPerMinutePerTenant { get; set; } = 600;
}
