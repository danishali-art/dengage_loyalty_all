namespace dEngage.Loyalty.Api.Framework.Options;

public sealed class ApiOptions
{
    public string ConnectionString { get; set; } = default!;
    public string RedisConnectionString { get; set; } = default!;
}
