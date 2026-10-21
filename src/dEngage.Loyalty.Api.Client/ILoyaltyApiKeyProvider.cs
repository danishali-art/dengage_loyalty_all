namespace dEngage.Loyalty.Api.Client;

// Public.Api authenticates every call with the tenant's own API key (issued by a platform admin in
// the loyalty portal). A caller that serves many tenants, like the dEngage connector, provides one
// per tenant; StaticLoyaltyApiKeyProvider covers a fixed list.
public interface ILoyaltyApiKeyProvider
{
    // `tenantId` is the loyalty tenant slug the call is for.
    Task<string> GetApiKeyAsync(string tenantId, CancellationToken ct);
}

public sealed class StaticLoyaltyApiKeyProvider : ILoyaltyApiKeyProvider
{
    private readonly IReadOnlyDictionary<string, string> _apiKeysByTenant;

    public StaticLoyaltyApiKeyProvider(IReadOnlyDictionary<string, string> apiKeysByTenant)
    {
        _apiKeysByTenant = apiKeysByTenant ?? throw new ArgumentNullException(nameof(apiKeysByTenant));
    }

    public Task<string> GetApiKeyAsync(string tenantId, CancellationToken ct) =>
        _apiKeysByTenant.TryGetValue(tenantId, out var key) && !string.IsNullOrWhiteSpace(key)
            ? Task.FromResult(key)
            : throw new InvalidOperationException($"No loyalty API key is configured for tenant '{tenantId}'.");
}
