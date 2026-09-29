namespace dEngage.Loyalty.Api.Framework.Options;

public sealed class JwtOptions
{
    public string SigningKey { get; set; } = default!;
    public string Issuer { get; set; } = "loyalty-api";
    public string Audience { get; set; } = "loyalty-dashboard";
    public int ExpiryMinutes { get; set; } = 60;
}
