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
using dEngage.Loyalty.Shared.Messaging;
using dEngage.Loyalty.Shared.Security;
using dEngage.Common.AspNetCore;
using dEngage.Common.AspNetCore.Extensions;
using dEngage.Common.AspNetCore.Filters;
using dEngage.Common.AspNetCore.Settings;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Mvc;
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
        services.AddOptions<RabbitMqOptions>().Bind(configuration.GetSection("RabbitMq"));
        // The broker connection: the platform's RabbitMQSettings, else the legacy RabbitMq keys (D8).
        services.AddOptions<BrokerConnectionSettings>().Configure(s => s.LoadFrom(configuration)).ValidateOnStart();
        services.AddSingleton<IValidateOptions<ApiOptions>, ApiOptionsValidator>();
        services.AddSingleton<IValidateOptions<BrokerConnectionSettings>, BrokerConnectionSettingsValidator>();

        services.AddDbContext<LoyaltyDbContext>((sp, options) =>
            options.UseNpgsql(sp.GetRequiredService<IOptions<ApiOptions>>().Value.ConnectionString));

        services.AddSingleton<IConnectionMultiplexer>(sp =>
            ConnectionMultiplexer.Connect(sp.GetRequiredService<IOptions<ApiOptions>>().Value.RedisConnectionString));

        services.AddSingleton<IRabbitMqConnectionHolder>(sp =>
            new RabbitMqConnectionHolder(sp.GetRequiredService<IOptions<BrokerConnectionSettings>>().Value));
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

        // Controllers on the platform's MVC setup (dEngage.Common.AspNetCore: log events, controller
        // debug view, Swagger behind SwaggerSettings:IsActive). Its JSON formatters (Newtonsoft) never
        // touch loyalty's bodies: requests are read and responses written by JsonResponses with
        // JsonConventions. Its GeneralExceptionFilter is removed, because it answers errors with
        // { "message": … }; LoyaltyApiMiddleware writes the API's ProblemDetails body (with the `code`
        // the portals and Api.Client rely on) instead.
        services.AddDengageCommonAspNetCoreModule(mvcSetupAction: mvc =>
        {
            foreach (var filter in mvc.Filters.OfType<TypeFilterAttribute>()
                         .Where(f => f.ImplementationType == typeof(GeneralExceptionFilter)).ToList())
                mvc.Filters.Remove(filter);
        });
        services.AddCors();

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

    // The request pipeline, on the platform's external-API defaults (forwarded headers, trace id, log
    // events, routing, Swagger when enabled, CORS, controllers; outside Development the platform's
    // internal endpoints are switched off). LoyaltyApiMiddleware runs after routing, before the
    // endpoints: correlation id, credential resolution and ProblemDetails error bodies for /api.
    //
    // The platform's request/response logger is OFF on purpose: it logs every header and the first
    // 1 KB of every body, which here means portal JWTs, raw API keys, login passwords and customer
    // data in Graylog/Seq.
    public static void UseLoyaltyApi(this WebApplication app, Action<CorsPolicyBuilder>? corsPolicy)
    {
        app.UseExternalApiDengageCommonDefaults(app.Environment, app.Configuration,
            corsPolicyAction: corsPolicy,
            swaggerSettings: app.Services.GetRequiredService<ISwaggerSettings>(),
            useLogEventLogger: true,
            useRequestResponseLogger: false,
            preAuthenticationAction: pipeline => pipeline.UseMiddleware<LoyaltyApiMiddleware>(),
            configureEndpoints: endpoints => endpoints.MapLoyaltyHealthChecks());
    }
}
