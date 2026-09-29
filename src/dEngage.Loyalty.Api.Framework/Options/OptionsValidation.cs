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

public sealed class RabbitMqOptionsValidator : IValidateOptions<RabbitMqOptions>
{
    public ValidateOptionsResult Validate(string? name, RabbitMqOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Host))
            return ValidateOptionsResult.Fail("RabbitMq:Host must be set.");
        return ValidateOptionsResult.Success;
    }
}
