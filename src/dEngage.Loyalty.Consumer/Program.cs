using dEngage.Loyalty.Consumer;
using dEngage.Loyalty.Consumer.Handlers;
using dEngage.Loyalty.Engine.Framework.Bootstrap;
using dEngage.Loyalty.Engine.Framework.Consumers;
using dEngage.Loyalty.Ledger;
using dEngage.Loyalty.RuleEngine;
using dEngage.Loyalty.RuleEngine.Cache;
using dEngage.Loyalty.RuleEngine.Calculation;
using dEngage.Loyalty.RuleEngine.Campaigns;
using dEngage.Loyalty.RuleEngine.Campaigns.Streak;
using dEngage.Loyalty.RuleEngine.Processing;
using RuleEngineService = dEngage.Loyalty.RuleEngine.RuleEngine;
using dEngage.Loyalty.Schema;
using dEngage.Loyalty.Shared.Security;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;
using dEngage.Common.Hosting;
using dEngage.Common.Utilities.Settings;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NLog;
using NLog.Web;

// Platform hosting (alignment D1): deployed environments mount their appsettings*.json into
// CONFIG_PATH (and may point NLOG_CONFIG_PATH elsewhere); locally both are unset and the files
// next to the binary are used. The platform host also runs a small Kestrel endpoint
// (appsettings.json "Kestrel") for /health-check and Prometheus metrics, like other dEngage
// services. It is cluster-internal, so the platform's internal endpoints stay switched on.
var appLocation = AppContext.BaseDirectory;
var configPath = Environment.GetEnvironmentVariable("CONFIG_PATH");
var bootLogger = LogManager.Setup()
    .LoadConfigurationFromAppSettings(Environment.GetEnvironmentVariable("NLOG_CONFIG_PATH") ?? configPath ?? appLocation, "logging")
    .GetCurrentClassLogger();

var host = DengageHostBuilder.CreateHostBuilder(bootLogger, appLocation, configPath)
    // The platform HostBuilder only reads ASPNETCORE_* for its environment; keep honouring
    // DOTNET_ENVIRONMENT (launchSettings) and --environment as Host.CreateApplicationBuilder did.
    .ConfigureHostConfiguration(hostConfig => hostConfig
        .AddEnvironmentVariables("DOTNET_")
        .AddCommandLine(args))
    .ConfigureAppConfiguration((_, appConfig) =>
    {
        // Resolve config values carrying ENC(...) tokens and layer them on top as an in-memory provider
        var encryptedEntries = appConfig.Build().AsEnumerable()
            .Where(kv => SecretCrypto.ContainsToken(kv.Value))
            .ToList();
        if (encryptedEntries.Count > 0)
        {
            var masterKey = SecretCrypto.LoadKeyFromEnv();
            appConfig.AddInMemoryCollection(encryptedEntries.ToDictionary(
                kv => kv.Key,
                kv => (string?)SecretCrypto.ResolveTokens(kv.Value!, masterKey)));
        }
    })
    .ConfigureServices((context, services) =>
    {
        var config = ConsumerConfig.FromConfiguration(context.Configuration);

        // The platform's startup filter (/health-check, /restart, debug-view) needs IAppSettings.
        services.TryAddSingleton<IAppSettings, AppSettings>();
        services.AddSingleton(config);
        services.AddLoyaltyEngineFramework();
        services.AddScoped<dEngage.Loyalty.Engine.Framework.Events.IEventPublisherSink, OutboxEventPublisherSink>();

        // DB
        services.AddDbContext<LoyaltyDbContext>(options =>
            options.UseNpgsql(config.ConnectionString));
        services.AddSingleton<TenantSlugCache>();
        services.AddScoped<ITenantSlugResolver, TenantSlugResolver>();

        // loyalty_config (platform alignment D4) and the protector for its secret rows (D3).
        services.AddSingleton(new LoyaltyConfigCache());
        services.AddSingleton<ISecretProtector, SecretCryptoProtector>();
        services.AddScoped<ILoyaltyConfig, LoyaltyConfigReader>();

        // Redis
        services.AddSingleton<IConnectionMultiplexer>(
            ConnectionMultiplexer.Connect(config.RedisConnectionString));

        // Ledger
        services.AddScoped<ILedgerService, LedgerService>();
        services.AddScoped<IRefundService, RefundService>();
        services.AddScoped<PointsExpirationJob>();
        services.AddScoped<PointsExpiringDetectorJob>();
        services.AddScoped<EventLogRetentionJob>();
        services.AddScoped<IOutboxService, OutboxService>();

        // RuleEngine
        services.AddScoped<IRuleCacheService, RuleCacheService>();
        services.AddScoped<ILimitCacheService, LimitCacheService>();
        services.AddSingleton<IRuleTypeHandler, SpendRuleHandler>();
        services.AddSingleton<IRuleTypeHandler, FixedBonusRuleHandler>();
        // CR-02: pipeline-compatible new rule types (TransferRule/ReversalRule bypass this registry
        // entirely — see WinnerSelector/RuleEngine.cs).
        services.AddSingleton<IRuleTypeHandler, RedemptionRuleHandler>();
        services.AddSingleton<IRuleTypeHandler, ManualAdjustmentRuleHandler>();
        services.AddSingleton<IRuleTypeHandlerRegistry, RuleTypeHandlerRegistry>();
        services.AddScoped<IRuleMatcher, RuleMatcher>();
        services.AddScoped<ITierContextLoader, TierContextLoader>();
        services.AddScoped<IRuleLimitEvaluator, RuleLimitEvaluator>();
        services.AddScoped<IBudgetReservationService, BudgetReservationService>();
        services.AddScoped<IWinnerSelector, WinnerSelector>();
        // CR 2026-09-30 (A2): pays out cashback / tier-upgrade rewards for RewardPurchaseHandler and
        // StreakCampaignModule.
        services.AddScoped<IRewardFulfilmentService, RewardFulfilmentService>();
        services.AddScoped<IRuleFireAuditWriter, RuleFireAuditWriter>();
        services.AddScoped<ILedgerPoster, LedgerPoster>();
        // CR-02: bypass WinnerSelector/LedgerPoster — see RuleEngine.cs dispatch and the
        // processor's class remarks for why (inherited target account).
        services.AddScoped<IReversalRuleProcessor, ReversalRuleProcessor>();
        // CR 2026-10-05: redeem / transfer rules are picked by their event handlers, never by the engine.
        services.AddScoped<IBurnRuleResolver, BurnRuleResolver>();
        services.AddScoped<DelayedPostingPromotionJob>();
        services.AddScoped<ILimitCounterSync, LimitCounterSync>();
        services.AddScoped<IRuleEngine, RuleEngineService>();
        services.AddScoped<ITierEvaluationService, TierEvaluationService>();
        services.AddScoped<TierDowngradeJob>();
        services.AddScoped<ICampaignEvaluationService, CampaignEvaluationService>();

        // Campaigns (streak today; any future campaign type follows the same ICampaignModule shape)
        services.AddScoped<ICampaignConfigCacheService, CampaignConfigCacheService>();
        services.AddScoped<StreakCampaignModule>();
        services.AddScoped<IStreakCampaignModule>(sp => sp.GetRequiredService<StreakCampaignModule>());
        services.AddScoped<ICampaignModule>(sp => sp.GetRequiredService<StreakCampaignModule>());
        services.AddScoped<ICampaignModuleRegistry, CampaignModuleRegistry>();
        services.AddScoped<StreakMaintenanceJob>();

        // Handlers
        services.AddScoped<IEventHandler, OrderCreatedHandler>();
        services.AddScoped<IEventHandler, OrderRefundedHandler>();
        services.AddScoped<IEventHandler, CashAddedHandler>();
        services.AddScoped<IEventHandler, CashSpentHandler>();
        services.AddScoped<IEventHandler, PointsRedeemHandler>();
        services.AddScoped<IEventHandler, PointsTransferHandler>();
        services.AddScoped<IEventHandler, RewardPurchaseHandler>();
        // CR-01/CR-02 (docs/scope-change-rules): new built-in events, all routed through the generic
        // rule-match pipeline (ICampaignEvaluationService), same shape as OrderCreatedHandler.
        services.AddScoped<IEventHandler, SignupHandler>();
        services.AddScoped<IEventHandler, KycCompletedHandler>();
        services.AddScoped<IEventHandler, CardTransactionHandler>();
        services.AddScoped<IEventHandler, RemittanceHandler>();
        services.AddScoped<IEventHandler, PointsAdjustedHandler>();
        services.AddScoped<IEventHandler, GenericEventHandler>();
        services.AddScoped<IEventHandlerRegistry, EventHandlerRegistry>();

        // Background services — RuleSync starts first, Consumer waits for the ready signal
        services.AddSingleton<RuleSyncService>();
        services.AddHostedService(sp => sp.GetRequiredService<RuleSyncService>());
        services.AddHostedService<EventConsumerWorker>();
        services.AddHostedService<TierDowngradeWorker>();
        services.AddHostedService<StreakMaintenanceWorker>();
        services.AddHostedService<PointsExpirationWorker>();
        services.AddHostedService<DelayedPostingPromotionWorker>();
        services.AddHostedService<PointsExpiringDetectorWorker>();
        services.AddHostedService<EventLogRetentionWorker>();
        services.AddHostedService<OutboxPublisherWorker>();
    })
    .Build();

try
{
    host.Run();
}
finally
{
    // Flush buffered NLog targets (Seq/Graylog batch) before the process exits.
    LogManager.Shutdown();
}
