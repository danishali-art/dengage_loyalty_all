using FluentValidation;
using dEngage.Loyalty.Api;
using dEngage.Loyalty.Api.AccountTypes;
using dEngage.Loyalty.Api.Auth;
using dEngage.Loyalty.Api.CardBuckets;
using dEngage.Loyalty.Api.ConfigVersions;
using dEngage.Loyalty.Api.Customers;
using dEngage.Loyalty.Api.Dashboard;
using dEngage.Loyalty.Api.Events;
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

var builder = WebApplication.CreateBuilder(args);

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
builder.Services.AddScoped<IEventsAppService, EventsAppService>();
builder.Services.AddScoped<IConfigVersionsAppService, ConfigVersionsAppService>();
builder.Services.AddSingleton<IConfigVersionService, ConfigVersionService>();
builder.Services.AddScoped<IDashboardAppService, DashboardAppService>();
builder.Services.AddScoped<IRuleCacheService, RuleCacheService>();
builder.Services.AddScoped<ICampaignConfigCacheService, CampaignConfigCacheService>();

builder.Services.AddSingleton<IAccountTypeConfigValidator, PointsAccountTypeConfigValidator>();
builder.Services.AddSingleton<IAccountTypeConfigValidator, CashAccountTypeConfigValidator>();
builder.Services.AddSingleton<AccountTypeConfigValidatorSelector>();

builder.Services.AddNancyModule<PingModule>();
builder.Services.AddNancyModule<TenantsModule>();
builder.Services.AddNancyModule<AuthModule>();
builder.Services.AddNancyModule<ProgramsModule>();
builder.Services.AddNancyModule<AccountTypesModule>();
builder.Services.AddNancyModule<TiersModule>();
builder.Services.AddNancyModule<RewardsModule>();
builder.Services.AddNancyModule<RulesModule>();
builder.Services.AddNancyModule<StreakCampaignsModule>();
builder.Services.AddNancyModule<CardBucketsModule>();
builder.Services.AddNancyModule<CustomersModule>();
builder.Services.AddNancyModule<EventsModule>();
builder.Services.AddNancyModule<ConfigVersionsModule>();
builder.Services.AddNancyModule<DashboardModule>();

// Environment isolation (plan §5): strict CORS outside Development, generic error bodies enforced
// by CompositionRootNancyBootstrapper's OnError hook via IHostEnvironment.IsDevelopment().
if (!builder.Environment.IsDevelopment())
{
    builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
        policy.WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [])
            .AllowAnyHeader()
            .AllowAnyMethod()));
}

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseCors();
    app.UseHttpsRedirection();
}

app.MapHealthChecks();
app.UseLoyaltyApiFramework();

app.Run();

public partial class Program;
