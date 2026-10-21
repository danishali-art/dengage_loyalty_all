using FluentValidation;
using dEngage.Loyalty.Api.AccountTypes;
using dEngage.Loyalty.Api.Auth;
using dEngage.Loyalty.Api.CardBuckets;
using dEngage.Loyalty.Api.ConfigVersions;
using dEngage.Loyalty.Api.Customers;
using dEngage.Loyalty.Api.Dashboard;
using dEngage.Loyalty.Api.Framework.Bootstrap;
using dEngage.Loyalty.Api.Modules;
using dEngage.Loyalty.Api.Platform;
using dEngage.Loyalty.Api.Programs;
using dEngage.Loyalty.Api.Rewards;
using dEngage.Loyalty.Api.Rules;
using dEngage.Loyalty.Api.StreakCampaigns;
using dEngage.Loyalty.Api.Tiers;
using dEngage.Loyalty.RuleEngine.Cache;
using dEngage.Loyalty.RuleEngine.Campaigns;
using dEngage.Loyalty.Shared.Security;
using dEngage.Common.Hosting;
using dEngage.Common.Shared.Constants;
using dEngage.Common.Utilities.Settings;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NLog;
using NLog.Web;

// DengageWebApplicationBuilder reads the environment from WEB_APPLICATION_ENVIRONMENT only and
// falls back to Production. Resolve it the standard .NET way first (--environment, then
// ASPNETCORE_/DOTNET_ENVIRONMENT) so launch profiles, containers and WebApplicationFactory's
// UseEnvironment (passed in as --environment) keep selecting Development/Staging/Testing.
if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WEB_APPLICATION_ENVIRONMENT")))
    Environment.SetEnvironmentVariable("WEB_APPLICATION_ENVIRONMENT", new ConfigurationBuilder()
        .AddEnvironmentVariables("DOTNET_")
        .AddEnvironmentVariables("ASPNETCORE_")
        .AddCommandLine(args)
        .Build()["environment"]);

// Platform hosting (alignment D1): deployed environments mount their appsettings*.json into
// CONFIG_PATH (and may point NLOG_CONFIG_PATH elsewhere); locally both are unset and the files
// next to the binary are used.
var appLocation = AppContext.BaseDirectory;
var configPath = Environment.GetEnvironmentVariable("CONFIG_PATH");
var bootLogger = LogManager.Setup()
    .LoadConfigurationFromAppSettings(Environment.GetEnvironmentVariable("NLOG_CONFIG_PATH") ?? configPath ?? appLocation, "logging")
    .GetCurrentClassLogger();

var builder = DengageWebApplicationBuilder.CreateBuilder(args, bootLogger, currentLocation: appLocation, configLocation: configPath);

// The platform's startup filter (/health-check, /restart, debug-view) needs IAppSettings.
builder.Services.TryAddSingleton<IAppSettings, AppSettings>();

// This API is reached from browsers and client systems. Outside Development, switch off the
// platform's internal endpoints: /restart would let any caller stop the process and debug-view
// prints the configuration (same rule as dEngage.Common.AspNetCore's external-API defaults).
if (!builder.Environment.IsDevelopment())
    CommonConstants.MarkAsExternal();

// Same ENC(...) config-secret resolution pass as Consumer's Program.cs (see dEngage.Loyalty.Shared.Security.SecretCrypto)
var encryptedEntries = builder.Configuration.AsEnumerable()
    .Where(kv => SecretCrypto.ContainsToken(kv.Value))
    .ToList();
if (encryptedEntries.Count > 0)
{
    var masterKey = SecretCrypto.LoadKeyFromEnv();
    builder.Configuration.AddInMemoryCollection(encryptedEntries.ToDictionary(
        kv => kv.Key,
        kv => (string?)SecretCrypto.ResolveTokens(kv.Value!, masterKey)));
}

builder.Services.AddLoyaltyApiFramework(builder.Configuration);
builder.Services.AddLoyaltyJwtAuthentication(builder.Configuration);
builder.Services.AddValidatorsFromAssemblyContaining<Program>();

builder.Services.AddScoped<IPlatformAppService, PlatformAppService>();
builder.Services.AddScoped<IAuthAppService, AuthAppService>();
builder.Services.AddScoped<IProgramsAppService, ProgramsAppService>();
builder.Services.AddScoped<IProgramChangeTracker, ProgramChangeTracker>();
builder.Services.AddScoped<IAccountTypesAppService, AccountTypesAppService>();
builder.Services.AddScoped<ITiersAppService, TiersAppService>();
builder.Services.AddScoped<IRewardsAppService, RewardsAppService>();
builder.Services.AddScoped<IRulesAppService, RulesAppService>();
builder.Services.AddScoped<IRuleVersioningService, RuleVersioningService>();
builder.Services.AddScoped<IStreakCampaignsAppService, StreakCampaignsAppService>();
builder.Services.AddScoped<ICardBucketsAppService, CardBucketsAppService>();
builder.Services.AddScoped<ICustomersAppService, CustomersAppService>();
builder.Services.AddScoped<IConfigVersionsAppService, ConfigVersionsAppService>();
builder.Services.AddSingleton<IConfigVersionService, ConfigVersionService>();
builder.Services.AddScoped<IDashboardAppService, DashboardAppService>();
builder.Services.AddScoped<IRuleCacheService, RuleCacheService>();
builder.Services.AddScoped<ICampaignConfigCacheService, CampaignConfigCacheService>();

builder.Services.AddSingleton<IAccountTypeConfigValidator, PointsAccountTypeConfigValidator>();
builder.Services.AddSingleton<IAccountTypeConfigValidator, CashAccountTypeConfigValidator>();
builder.Services.AddSingleton<AccountTypeConfigValidatorSelector>();

var app = builder.Build();

// Environment isolation (plan §5): strict CORS outside Development (the portal's dev server proxies
// /api instead), generic error bodies enforced by LoyaltyApiMiddleware via IsDevelopment().
var allowedOrigins = app.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
if (!app.Environment.IsDevelopment())
    app.UseHttpsRedirection();

app.UseLoyaltyApi(corsPolicy: app.Environment.IsDevelopment()
    ? null
    : policy => policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod());

try
{
    app.Run();
}
finally
{
    // Flush buffered NLog targets (Seq/Graylog batch) before the process exits.
    LogManager.Shutdown();
}

public partial class Program;
