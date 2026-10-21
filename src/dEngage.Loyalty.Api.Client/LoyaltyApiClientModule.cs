using Microsoft.Extensions.DependencyInjection;

namespace dEngage.Loyalty.Api.Client;

// Registration, in the shape of the platform's other *ApiClientModule classes. `publicApiBaseUrl`
// is where dEngage.Loyalty.Public.Api is served, for example "https://loyalty-public.example.com"
// (a path prefix is fine). The returned builder allows extra handlers (retries, logging).
public static class LoyaltyApiClientModule
{
    // A fixed list of API keys, by tenant slug.
    public static IHttpClientBuilder AddLoyaltyApiClient(this IServiceCollection services,
        string publicApiBaseUrl,
        IReadOnlyDictionary<string, string> apiKeysByTenant,
        int httpClientLifetimeMinutes = 5,
        int timeoutSeconds = 30)
    {
        services.AddSingleton<ILoyaltyApiKeyProvider>(new StaticLoyaltyApiKeyProvider(apiKeysByTenant));
        return AddClient(services, publicApiBaseUrl, httpClientLifetimeMinutes, timeoutSeconds);
    }

    // API keys from the caller's own store (for example one per connected tenant).
    public static IHttpClientBuilder AddLoyaltyApiClient<TApiKeyProvider>(this IServiceCollection services,
        string publicApiBaseUrl,
        int httpClientLifetimeMinutes = 5,
        int timeoutSeconds = 30)
        where TApiKeyProvider : class, ILoyaltyApiKeyProvider
    {
        services.AddSingleton<ILoyaltyApiKeyProvider, TApiKeyProvider>();
        return AddClient(services, publicApiBaseUrl, httpClientLifetimeMinutes, timeoutSeconds);
    }

    private static IHttpClientBuilder AddClient(IServiceCollection services, string publicApiBaseUrl,
        int httpClientLifetimeMinutes, int timeoutSeconds)
    {
        if (string.IsNullOrWhiteSpace(publicApiBaseUrl))
            throw new ArgumentException("The loyalty Public API base URL is required.", nameof(publicApiBaseUrl));

        // Request paths are relative, so the base must end with "/" or its last segment is dropped.
        var baseAddress = new Uri(publicApiBaseUrl.EndsWith("/", StringComparison.Ordinal) ? publicApiBaseUrl : publicApiBaseUrl + "/");

        return services
            .AddHttpClient<ILoyaltyApiClient, LoyaltyApiClient>(client =>
            {
                client.BaseAddress = baseAddress;
                client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
            })
            .SetHandlerLifetime(TimeSpan.FromMinutes(httpClientLifetimeMinutes));
    }
}
