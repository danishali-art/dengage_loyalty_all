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

var builder = Host.CreateApplicationBuilder(args);

// Resolve config values carrying ENC(...) tokens and layer them on top as an in-memory provider
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

var config = new ConsumerConfig
{
    ConnectionString = builder.Configuration["ConnectionString"]
        ?? "Host=localhost;Database=loyalty;Username=postgres;Password=postgres",
    RedisConnectionString = builder.Configuration["Redis"]
        ?? "localhost:6379",
    RabbitMqHost = builder.Configuration["RabbitMq:Host"] ?? "localhost",
    RabbitMqPort = int.TryParse(builder.Configuration["RabbitMq:Port"], out var port) ? port : 5672,
    RabbitMqUser = builder.Configuration["RabbitMq:User"] ?? "guest",
    RabbitMqPassword = builder.Configuration["RabbitMq:Password"] ?? "guest",
    GenericEventTypes = builder.Configuration.GetSection("RabbitMq:GenericEventTypes").Get<string[]>() ?? Array.Empty<string>()
};

builder.Services.AddSingleton(config);
builder.Services.AddLoyaltyEngineFramework();
builder.Services.AddScoped<dEngage.Loyalty.Engine.Framework.Events.IEventPublisherSink, OutboxEventPublisherSink>();

// DB
builder.Services.AddDbContext<LoyaltyDbContext>(options =>
    options.UseNpgsql(config.ConnectionString));
builder.Services.AddSingleton<TenantSlugCache>();
builder.Services.AddScoped<ITenantSlugResolver, TenantSlugResolver>();

// Redis
builder.Services.AddSingleton<IConnectionMultiplexer>(
    ConnectionMultiplexer.Connect(config.RedisConnectionString));

// Ledger
builder.Services.AddScoped<ILedgerService, LedgerService>();
builder.Services.AddScoped<IRefundService, RefundService>();
builder.Services.AddScoped<PointsExpirationJob>();
builder.Services.AddScoped<PointsExpiringDetectorJob>();
builder.Services.AddScoped<EventLogRetentionJob>();
builder.Services.AddScoped<IOutboxService, OutboxService>();

// RuleEngine
builder.Services.AddScoped<IRuleCacheService, RuleCacheService>();
builder.Services.AddScoped<ILimitCacheService, LimitCacheService>();
builder.Services.AddSingleton<IRuleTypeHandler, SpendRuleHandler>();
builder.Services.AddSingleton<IRuleTypeHandler, StampRuleHandler>();
builder.Services.AddSingleton<IRuleTypeHandler, FixedBonusRuleHandler>();
// CR-02: pipeline-compatible new rule types (TransferRule/ReversalRule bypass this registry
// entirely — see WinnerSelector/RuleEngine.cs).
builder.Services.AddSingleton<IRuleTypeHandler, RedemptionRuleHandler>();
builder.Services.AddSingleton<IRuleTypeHandler, ExpiryRuleHandler>();
builder.Services.AddSingleton<IRuleTypeHandler, ManualAdjustmentRuleHandler>();
builder.Services.AddSingleton<IRuleTypeHandlerRegistry, RuleTypeHandlerRegistry>();
builder.Services.AddScoped<IRuleMatcher, RuleMatcher>();
builder.Services.AddScoped<ITierContextLoader, TierContextLoader>();
builder.Services.AddScoped<IRuleLimitEvaluator, RuleLimitEvaluator>();
builder.Services.AddScoped<IBudgetReservationService, BudgetReservationService>();
builder.Services.AddScoped<IWinnerSelector, WinnerSelector>();
builder.Services.AddScoped<IStampCompletionHandler, StampCompletionHandler>();
// CR 2026-09-30 (A2): pays out cashback / tier-upgrade rewards for RewardPurchaseHandler and
// StreakCampaignModule.
builder.Services.AddScoped<IRewardFulfilmentService, RewardFulfilmentService>();
builder.Services.AddScoped<IRuleFireAuditWriter, RuleFireAuditWriter>();
builder.Services.AddScoped<ILedgerPoster, LedgerPoster>();
// CR-02: bypass WinnerSelector/LedgerPoster — see RuleEngine.cs dispatch and each
// processor's class remarks for why (dual-entry posting / inherited target account).
builder.Services.AddScoped<ITransferRuleProcessor, TransferRuleProcessor>();
builder.Services.AddScoped<IReversalRuleProcessor, ReversalRuleProcessor>();
builder.Services.AddScoped<DelayedPostingPromotionJob>();
builder.Services.AddScoped<BirthdayBonusJob>();
builder.Services.AddScoped<ILimitCounterSync, LimitCounterSync>();
builder.Services.AddScoped<IRuleEngine, RuleEngineService>();
builder.Services.AddScoped<ITierEvaluationService, TierEvaluationService>();
builder.Services.AddScoped<TierDowngradeJob>();
builder.Services.AddScoped<ICampaignEvaluationService, CampaignEvaluationService>();

// Campaigns (streak today; any future campaign type follows the same ICampaignModule shape)
builder.Services.AddScoped<ICampaignConfigCacheService, CampaignConfigCacheService>();
builder.Services.AddScoped<StreakCampaignModule>();
builder.Services.AddScoped<IStreakCampaignModule>(sp => sp.GetRequiredService<StreakCampaignModule>());
builder.Services.AddScoped<ICampaignModule>(sp => sp.GetRequiredService<StreakCampaignModule>());
builder.Services.AddScoped<ICampaignModuleRegistry, CampaignModuleRegistry>();
builder.Services.AddScoped<StreakMaintenanceJob>();

// Handlers
builder.Services.AddScoped<IEventHandler, OrderCreatedHandler>();
builder.Services.AddScoped<IEventHandler, OrderRefundedHandler>();
builder.Services.AddScoped<IEventHandler, CashAddedHandler>();
builder.Services.AddScoped<IEventHandler, CashSpentHandler>();
builder.Services.AddScoped<IEventHandler, PointsRedeemHandler>();
builder.Services.AddScoped<IEventHandler, PointsTransferHandler>();
builder.Services.AddScoped<IEventHandler, RewardPurchaseHandler>();
// CR-01/CR-02 (docs/scope-change-rules): new built-in events, all routed through the generic
// rule-match pipeline (ICampaignEvaluationService), same shape as OrderCreatedHandler.
builder.Services.AddScoped<IEventHandler, SignupHandler>();
builder.Services.AddScoped<IEventHandler, KycCompletedHandler>();
builder.Services.AddScoped<IEventHandler, CardTransactionHandler>();
builder.Services.AddScoped<IEventHandler, RemittanceHandler>();
builder.Services.AddScoped<IEventHandler, PointsAdjustedHandler>();
builder.Services.AddScoped<IEventHandler, PointsExpiredHandler>();
builder.Services.AddScoped<IEventHandler, GenericEventHandler>();
builder.Services.AddScoped<IEventHandlerRegistry, EventHandlerRegistry>();

// Background services — RuleSync starts first, Consumer waits for the ready signal
builder.Services.AddSingleton<RuleSyncService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<RuleSyncService>());
builder.Services.AddHostedService<EventConsumerWorker>();
builder.Services.AddHostedService<TierDowngradeWorker>();
builder.Services.AddHostedService<StreakMaintenanceWorker>();
builder.Services.AddHostedService<PointsExpirationWorker>();
builder.Services.AddHostedService<DelayedPostingPromotionWorker>();
builder.Services.AddHostedService<BirthdayBonusWorker>();
builder.Services.AddHostedService<PointsExpiringDetectorWorker>();
builder.Services.AddHostedService<EventLogRetentionWorker>();
builder.Services.AddHostedService<OutboxPublisherWorker>();

var host = builder.Build();
host.Run();
