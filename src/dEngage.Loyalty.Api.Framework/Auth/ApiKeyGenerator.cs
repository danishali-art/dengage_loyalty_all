using System.Security.Cryptography;

namespace dEngage.Loyalty.Api.Framework.Auth;

public sealed record GeneratedApiKey(string RawKey, string Prefix, string HashedKey);

// Raw key is returned once, at creation time, and never stored — only its hash + a short, non-secret
// prefix (for display in key-management UIs) persist to tenant_api_keys.
public interface IApiKeyGenerator
{
    GeneratedApiKey Generate(string tenantId);
}

public sealed class ApiKeyGenerator(IPasswordService passwordService) : IApiKeyGenerator
{
    public GeneratedApiKey Generate(string tenantId)
    {
        var secretBytes = RandomNumberGenerator.GetBytes(32);
        var secret = Convert.ToBase64String(secretBytes)
            .Replace("+", "").Replace("/", "").Replace("=", "");

        var prefix = $"lk_{tenantId}_{secret[..8]}".ToLowerInvariant();
        var rawKey = $"{prefix}.{secret[8..]}";

        return new GeneratedApiKey(rawKey, prefix, passwordService.Hash(rawKey));
    }
}
