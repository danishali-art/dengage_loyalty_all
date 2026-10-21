using dEngage.Loyalty.Shared.Messaging;
using Microsoft.Extensions.Options;

namespace dEngage.Loyalty.Api.Framework.Options;

// Fail-fast at host startup rather than on first request — see plan §5 (environment isolation).
public sealed class JwtOptionsValidator : IValidateOptions<JwtOptions>
{
    public ValidateOptionsResult Validate(string? name, JwtOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.SigningKey) || options.SigningKey.Length < 32)
            return ValidateOptionsResult.Fail("Jwt:SigningKey must be set and at least 32 characters.");
        return ValidateOptionsResult.Success;
    }
}

public sealed class ApiOptionsValidator : IValidateOptions<ApiOptions>
{
    public ValidateOptionsResult Validate(string? name, ApiOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ConnectionString))
            return ValidateOptionsResult.Fail("ConnectionString must be set.");
        if (string.IsNullOrWhiteSpace(options.RedisConnectionString))
            return ValidateOptionsResult.Fail("RedisConnectionString must be set.");
        return ValidateOptionsResult.Success;
    }
}

// No localhost or guest/guest fallback: a deployment without broker settings must not start
// (the old RabbitMqOptions defaulted to both, so a missing value went unnoticed).
public sealed class BrokerConnectionSettingsValidator : IValidateOptions<BrokerConnectionSettings>
{
    public ValidateOptionsResult Validate(string? name, BrokerConnectionSettings options)
    {
        if (options.Problems.Count > 0)
            return ValidateOptionsResult.Fail(
                "RabbitMQ connection settings are incomplete or invalid. Fix: " + string.Join(", ", options.Problems) + ".");
        return ValidateOptionsResult.Success;
    }
}
