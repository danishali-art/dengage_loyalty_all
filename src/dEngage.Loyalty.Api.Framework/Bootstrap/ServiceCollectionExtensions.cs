using dEngage.Loyalty.Api.Framework.Auth;
using dEngage.Loyalty.Api.Framework.Data;
using dEngage.Loyalty.Api.Framework.Messaging;
using dEngage.Loyalty.Api.Framework.Options;
using dEngage.Loyalty.Api.Framework.RateLimiting;
using dEngage.Loyalty.Api.Framework.Tenancy;
using dEngage.Loyalty.Schema;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Nancy;
using StackExchange.Redis;

namespace dEngage.Loyalty.Api.Framework.Bootstrap;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddLoyaltyApiFramework(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ApiOptions>().Bind(configuration).ValidateOnStart();
        services.AddOptions<JwtOptions>().Bind(configuration.GetSection("Jwt")).ValidateOnStart();
        services.AddOptions<RabbitMqOptions>().Bind(configuration.GetSection("RabbitMq")).ValidateOnStart();
        services.AddOptions<RateLimitOptions>().Bind(configuration.GetSection("RateLimit")).ValidateOnStart();
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

        services.AddSingleton<IRateLimiter, RedisRateLimiter>();
        services.AddSingleton<ITenantContextAccessor, TenantContextAccessor>();
        services.AddSingleton<IJwtTokenService, JwtTokenService>();
        services.AddSingleton<IPasswordService, PasswordService>();
        services.AddSingleton<IApiKeyGenerator, ApiKeyGenerator>();

        services.AddSingleton(sp =>
        {
            var bootstrapper = new CompositionRootNancyBootstrapper(sp, sp.GetRequiredService<IHostEnvironment>());
            bootstrapper.Initialise();
            return bootstrapper;
        });
        services.AddSingleton<INancyEngine>(sp => sp.GetRequiredService<CompositionRootNancyBootstrapper>().GetEngine());

        return services;
    }

    // Registers a Nancy module both under its concrete type (GetModule resolves by Type) and as
    // INancyModule (GetAllModules enumerates these once at startup to build the route table).
    // Transient, not Scoped: Nancy's one-time startup route discovery resolves modules from the
    // root provider (no request scope exists yet), and ASP.NET Core's dev-time scope validation
    // rejects a Scoped resolution from root. The module's own dependencies still resolve from
    // whichever provider is ambient (root at startup, the per-request scope on real requests).
    public static IServiceCollection AddNancyModule<TModule>(this IServiceCollection services)
        where TModule : class, INancyModule
    {
        services.AddTransient<TModule>();
        services.AddTransient<INancyModule>(sp => sp.GetRequiredService<TModule>());
        NancyModuleTypeRegistry.Register(typeof(TModule));
        return services;
    }

    public static IApplicationBuilder UseLoyaltyApiFramework(this IApplicationBuilder app) =>
        app.UseMiddleware<NancyAspNetCoreMiddleware>();
}
