using dEngage.Loyalty.Api.Framework.Auth;
using dEngage.Loyalty.Api.Framework.Controllers;
using dEngage.Loyalty.Api.Framework.Data;
using dEngage.Loyalty.Api.Framework.Messaging;
using dEngage.Loyalty.Api.Framework.Options;
using dEngage.Loyalty.Api.Framework.RateLimiting;
using dEngage.Loyalty.Api.Framework.Tenancy;
using dEngage.Loyalty.Schema;
using dEngage.Loyalty.Shared.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace dEngage.Loyalty.Api.Framework.Bootstrap;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddLoyaltyApiFramework(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ApiOptions>().Bind(configuration).ValidateOnStart();
        services.AddOptions<JwtOptions>().Bind(configuration.GetSection("Jwt")).ValidateOnStart();
        services.AddOptions<RabbitMqOptions>().Bind(configuration.GetSection("RabbitMq")).ValidateOnStart();
        services.AddSingleton<IValidateOptions<ApiOptions>, ApiOptionsValidator>();
        services.AddSingleton<IValidateOptions<JwtOptions>, JwtOptionsValidator>();
        services.AddSingleton<IValidateOptions<RabbitMqOptions>, RabbitMqOptionsValidator>();

        services.AddDbContext<LoyaltyDbContext>((sp, options) =>
            options.UseNpgsql(sp.GetRequiredService<IOptions<ApiOptions>>().Value.ConnectionString));

        services.AddSingleton<IConnectionMultiplexer>(sp =>
            ConnectionMultiplexer.Connect(sp.GetRequiredService<IOptions<ApiOptions>>().Value.RedisConnectionString));

        services.AddSingleton<IRabbitMqConnectionHolder>(sp =>
            new RabbitMqConnectionHolder(sp.GetRequiredService<IOptions<RabbitMqOptions>>().Value));
        services.AddScoped<IEventPublisher, RabbitMqEventPublisher>();

        services.AddScoped(typeof(IRepository<>), typeof(EfRepository<>));

        services.AddSingleton<TenantSlugCache>();
        services.AddScoped<ITenantSlugResolver, TenantSlugResolver>();

        // loyalty_config (platform alignment D4) and the protector for its secret rows (D3).
        services.AddSingleton(new LoyaltyConfigCache());
        services.AddSingleton<ISecretProtector, SecretCryptoProtector>();
        services.AddScoped<ILoyaltyConfig, LoyaltyConfigReader>();

        services.AddSingleton<IRateLimiter, RedisRateLimiter>();
        services.AddSingleton<ITenantContextAccessor, TenantContextAccessor>();
        services.AddSingleton<IJwtTokenService, JwtTokenService>();
        services.AddSingleton<IPasswordService, PasswordService>();
        services.AddSingleton<IApiKeyGenerator, ApiKeyGenerator>();

        // Controllers (platform alignment D7). No MVC formatters or model binding are relied on:
        // see LoyaltyControllerBase and JsonResponses.
        services.AddControllers();

        return services;
    }

    // Before the controllers: correlation id, credential resolution and ProblemDetails error
    // bodies for everything under /api (LoyaltyApiMiddleware).
    public static IApplicationBuilder UseLoyaltyApiFramework(this IApplicationBuilder app) =>
        app.UseMiddleware<LoyaltyApiMiddleware>();
}
