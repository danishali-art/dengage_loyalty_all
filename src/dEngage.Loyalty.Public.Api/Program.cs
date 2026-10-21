using dEngage.Loyalty.Api.Framework.Bootstrap;
using dEngage.Loyalty.Shared.Security;
using dEngage.Common.Hosting;
using dEngage.Common.Shared.Constants;
using dEngage.Common.Utilities.Settings;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NLog;
using NLog.Web;

// Same platform hosting as the admin Api (alignment D1); see dEngage.Loyalty.Api/Program.cs for
// why the environment is bridged into WEB_APPLICATION_ENVIRONMENT.
if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WEB_APPLICATION_ENVIRONMENT")))
    Environment.SetEnvironmentVariable("WEB_APPLICATION_ENVIRONMENT", new ConfigurationBuilder()
        .AddEnvironmentVariables("DOTNET_")
        .AddEnvironmentVariables("ASPNETCORE_")
        .AddCommandLine(args)
        .Build()["environment"]);

var appLocation = AppContext.BaseDirectory;
var configPath = Environment.GetEnvironmentVariable("CONFIG_PATH");
var bootLogger = LogManager.Setup()
    .LoadConfigurationFromAppSettings(Environment.GetEnvironmentVariable("NLOG_CONFIG_PATH") ?? configPath ?? appLocation, "logging")
    .GetCurrentClassLogger();

var builder = DengageWebApplicationBuilder.CreateBuilder(args, bootLogger, currentLocation: appLocation, configLocation: configPath);

builder.Services.TryAddSingleton<IAppSettings, AppSettings>();

// Reached by client systems: outside Development, switch off the platform's internal endpoints
// (/restart, debug-view), as the admin Api does.
if (!builder.Environment.IsDevelopment())
    CommonConstants.MarkAsExternal();

// Same ENC(...) config-secret resolution pass as the admin Api and the Consumer.
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

// API keys only: no AddLoyaltyJwtAuthentication, so no JWT signing key on the public service and
// a portal token is never accepted here. Server-to-server, so no CORS either.
builder.Services.AddLoyaltyApiFramework(builder.Configuration);

var app = builder.Build();

if (!app.Environment.IsDevelopment())
    app.UseHttpsRedirection();

app.UseLoyaltyApi(corsPolicy: null);

try
{
    app.Run();
}
finally
{
    // Flush buffered NLog targets (Seq/Graylog batch) before the process exits.
    LogManager.Shutdown();
}
