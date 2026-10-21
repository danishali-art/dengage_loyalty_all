using dEngage.Loyalty.Api.Framework.Auth;
using dEngage.Loyalty.Api.Framework.Controllers;
using dEngage.Loyalty.Api.Framework.Data;
using dEngage.Loyalty.Api.Framework.Events;
using dEngage.Loyalty.Api.Framework.Messaging;
using dEngage.Loyalty.Api.Framework.Options;
using dEngage.Loyalty.Api.Framework.RateLimiting;
using dEngage.Loyalty.Api.Framework.Tenancy;
using dEngage.Loyalty.Contract;
using dEngage.Loyalty.Schema;
using dEngage.Loyalty.Shared.Security;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace dEngage.Loyalty.Api.Framework.Bootstrap;

public static class ServiceCollectionExtensions
{
    // What both HTTP hosts (the admin Api and Public.Api) need: database, Redis, RabbitMQ,
    // tenancy, loyalty_config, API-key authentication, the event ingestion service and controllers.
    // JWT (portal logins) is separate, see AddLoyaltyJwtAuthentication.
    public static IServiceCollection AddLoyaltyApiFramework(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ApiOptions>().Bind(configuration).ValidateOnStart();
        services.AddOptions<RabbitMqOptions>().Bind(configuration.GetSection("RabbitMq")).ValidateOnStart();
        services.AddSingleton<IValidateOptions<ApiOptions>, ApiOptionsValidator>();
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
        services.AddSingleton<IPasswordService, PasswordService>();
        services.AddSingleton<IApiKeyGenerator, ApiKeyGenerator>();

        // Event ingestion (Public.Api) and the portal's simulate / types / status routes (Api).
        // Validators are listed here because each host scans only its own assembly for them.
        services.AddScoped<IEventsAppService, EventsAppService>();
        services.AddScoped<IValidator<OrderCreatedRequest>, OrderCreatedRequestValidator>();
        services.AddScoped<IValidator<OrderRefundedRequest>, OrderRefundedRequestValidator>();
        services.AddScoped<IValidator<CashAddedRequest>, CashAddedRequestValidator>();
        services.AddScoped<IValidator<CashSpentRequest>, CashSpentRequestValidator>();
        services.AddScoped<IValidator<PointsRedeemRequest>, PointsRedeemRequestValidator>();
        services.AddScoped<IValidator<PointsTransferRequest>, PointsTransferRequestValidator>();
        services.AddScoped<IValidator<RewardPurchaseRequest>, RewardPurchaseRequestValidator>();
        services.AddScoped<IValidator<GenericEventRequest>, GenericEventRequestValidator>();

        // Controllers (platform alignment D7). No MVC formatters or model binding are relied on:
        // see LoyaltyControllerBase and JsonResponses.
        services.AddControllers();

        return services;
    }

    // Portal logins: JWT issuing and validation. Only the admin Api calls this. Without it a bearer
    // token is ignored (LoyaltyApiMiddleware), so Public.Api accepts API keys only and needs no
    // signing key.
    public static IServiceCollection AddLoyaltyJwtAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<JwtOptions>().Bind(configuration.GetSection("Jwt")).ValidateOnStart();
        services.AddSingleton<IValidateOptions<JwtOptions>, JwtOptionsValidator>();
        services.AddSingleton<IJwtTokenService, JwtTokenService>();
        return services;
    }

    // Before the controllers: correlation id, credential resolution and ProblemDetails error
    // bodies for everything under /api (LoyaltyApiMiddleware).
    public static IApplicationBuilder UseLoyaltyApiFramework(this IApplicationBuilder app) =>
        app.UseMiddleware<LoyaltyApiMiddleware>();
}
