using dEngage.Loyalty.Ledger;
using dEngage.Loyalty.RuleEngine;
using dEngage.Loyalty.RuleEngine.Cache;
using dEngage.Loyalty.RuleEngine.Calculation;
using dEngage.Loyalty.RuleEngine.Campaigns;
using dEngage.Loyalty.RuleEngine.Models;
using dEngage.Loyalty.RuleEngine.Processing;
using dEngage.Loyalty.Schema;
using dEngage.Loyalty.Schema.Entities;
using dEngage.Loyalty.Shared;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using AccountTypeEntity = dEngage.Loyalty.Schema.Entities.AccountType;
using ProgramEntity = dEngage.Loyalty.Schema.Entities.Program;

namespace dEngage.Loyalty.IntegrationTests.Fixtures;

// Wires the real RuleEngine pipeline (RuleMatcher -> TierContextLoader -> WinnerSelector ->
// LedgerPoster -> LimitCounterSync -> TierEvaluationService) against a Sqlite in-memory
// LoyaltyDbContext, matching the production DI graph in Api/Program.cs. Only the two Redis-backed
// caches (rule/campaign config lookup, limit counters) are mocked — everything downstream of them
// is the real production code, so a passing test here exercises the same code path as prod.
public sealed class RuleEngineTestHarness : IDisposable
{
    private readonly SqliteConnection _connection;
    public LoyaltyDbContext Db { get; }
    public dEngage.Loyalty.RuleEngine.RuleEngine Engine { get; }
    public Mock<ILimitCacheService> LimitCache { get; } = new();

    private readonly List<CachedRule> _rules = new();
    private readonly List<CachedCampaignConfig> _campaigns = new();

    public const string TenantSlug = "t1";
    public readonly Guid TenantGuid = Guid.NewGuid();

    public RuleEngineTestHarness()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<LoyaltyDbContext>().UseSqlite(_connection).Options;
        Db = new LoyaltyDbContext(options);
        Db.Database.EnsureCreated();
        Db.Tenants.Add(new Tenant { Id = TenantGuid, Slug = TenantSlug, Name = TenantSlug, Status = TenantStatus.Active, CreatedAt = DateTime.UtcNow });
        Db.SaveChanges();

        var slugCache = new TenantSlugCache();
        var tenantSlugResolver = new TenantSlugResolver(Db, slugCache);
        var ledger = new LedgerService(Db, tenantSlugResolver);
        var outbox = new OutboxService(Db, tenantSlugResolver);

        var ruleCache = new Mock<IRuleCacheService>();
        ruleCache.Setup(c => c.GetRulesAsync(TenantSlug, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _rules.ToList());

        var campaignCache = new Mock<ICampaignConfigCacheService>();
        campaignCache.Setup(c => c.GetConfigsAsync(TenantSlug, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _campaigns.ToList());

        var handlerRegistry = new RuleTypeHandlerRegistry(new IRuleTypeHandler[]
        {
            new SpendRuleHandler(), new FixedBonusRuleHandler(),
            new RedemptionRuleHandler(), new ManualAdjustmentRuleHandler()
        });

        var matcher = new RuleMatcher(ruleCache.Object, campaignCache.Object);
        var tierContext = new TierContextLoader(Db, tenantSlugResolver);
        var limitEvaluator = new RuleLimitEvaluator(Db);
        var winner = new WinnerSelector(handlerRegistry, LimitCache.Object, limitEvaluator, NullLogger<WinnerSelector>.Instance);
        var auditWriter = new RuleFireAuditWriter(Db, tenantSlugResolver);
        var budgetReservation = new BudgetReservationService(Db, tenantSlugResolver, limitEvaluator);
        var ledgerPoster = new LedgerPoster(Db, ledger, outbox, auditWriter, budgetReservation, tenantSlugResolver, NullLogger<LedgerPoster>.Instance);
        var limitSync = new LimitCounterSync(LimitCache.Object, NullLogger<LimitCounterSync>.Instance);
        var tierEval = new TierEvaluationService(Db, outbox, tenantSlugResolver, NullLogger<TierEvaluationService>.Instance);
        var campaignModules = new CampaignModuleRegistry(Array.Empty<ICampaignModule>());
        var transferProcessor = new TransferRuleProcessor(Db, ledger, outbox, auditWriter, NullLogger<TransferRuleProcessor>.Instance);
        var reversalProcessor = new ReversalRuleProcessor(Db, ledger, outbox, auditWriter, budgetReservation, NullLogger<ReversalRuleProcessor>.Instance);

        Engine = new dEngage.Loyalty.RuleEngine.RuleEngine(
            matcher, tierContext, winner, ledgerPoster, limitSync, tierEval, campaignModules,
            transferProcessor, reversalProcessor, NullLogger<dEngage.Loyalty.RuleEngine.RuleEngine>.Instance);
    }

    // LedgerEntry.RuleId carries a real DB-level FK to rules — the cache lookup is mocked, but a
    // matching row must still exist for the ledger write itself to succeed.
    public void AddRule(CachedRule rule)
    {
        _rules.Add(rule);
        Db.Rules.Add(new Rule
        {
            Id = rule.Id,
            TenantId = TenantGuid,
            ProgramId = rule.ProgramId,
            Name = rule.Name,
            Type = rule.Type,
            Trigger = rule.Trigger,
            Conditions = rule.Conditions is null ? null : System.Text.Json.JsonSerializer.Serialize(rule.Conditions),
            Calculation = System.Text.Json.JsonSerializer.Serialize(rule.Calculation),
            TargetAccountTypeId = rule.TargetAccountTypeId,
            Limits = rule.Limits is null ? null : System.Text.Json.JsonSerializer.Serialize(rule.Limits),
            Priority = rule.Priority,
            Stackable = rule.Stackable,
            ExclusivityGroup = rule.ExclusivityGroup,
            StackMode = rule.StackMode,
            ActiveFrom = rule.ActiveFrom,
            ActiveTo = rule.ActiveTo,
            CurrentVersion = rule.Version == 0 ? 1 : rule.Version,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        Db.SaveChanges();
    }

    public void AddCampaign(CachedCampaignConfig config) => _campaigns.Add(config);

    // Seeds an event_log row as the Consumer would have written before dispatching to the
    // engine — required for any rule using an occurred_within condition to find its anchor event.
    public void LogEvent(string eventId, string contactKey, string eventType, DateTime occurredAt)
    {
        Db.EventLog.Add(new EventLog
        {
            TenantId = TenantSlug,
            EventId = eventId,
            ContactKey = contactKey,
            EventType = eventType,
            OccurredAt = occurredAt
        });
        Db.SaveChanges();
    }

    public Guid AddAccountType(Guid programId, string type, string name, bool isTierQualifying = false, string config = "{}")
    {
        var id = Guid.NewGuid();
        Db.AccountTypes.Add(new AccountTypeEntity { Id = id, TenantId = TenantGuid, ProgramId = programId, Type = type, Name = name, Config = config, IsTierQualifying = isTierQualifying, CreatedAt = DateTime.UtcNow });
        Db.SaveChanges();
        return id;
    }

    public Guid AddProgram()
    {
        var id = Guid.NewGuid();
        Db.Programs.Add(new ProgramEntity { Id = id, TenantId = TenantGuid, Name = "p1", Status = ProgramStatus.Active, PublicationStatus = ProgramPublicationStatus.Published, CreatedAt = DateTime.UtcNow });
        Db.SaveChanges();
        return id;
    }

    // AsNoTracking is required here: LedgerService applies balance changes via ExecuteUpdateAsync,
    // which bypasses the change tracker — a tracked query would return the stale in-memory value
    // from the account's original insert/upsert rather than what is actually in the database.
    public decimal GetBalance(string contactKey, Guid accountTypeId) =>
        Db.CustomerAccounts.AsNoTracking().SingleOrDefault(a => a.TenantId == TenantGuid && a.ContactKey == contactKey && a.AccountTypeId == accountTypeId)?.Balance ?? 0m;

    public int LedgerCount(string contactKey, Guid accountTypeId, string reason) =>
        Db.LedgerEntries.AsNoTracking().Count(l => l.ContactKey == contactKey && l.Reason == reason &&
            Db.CustomerAccounts.AsNoTracking().Any(a => a.Id == l.CustomerAccountId && a.AccountTypeId == accountTypeId));

    public bool OutboxHasDedupKey(string dedupKey) => Db.OutboxEvents.AsNoTracking().Any(o => o.TenantId == TenantGuid && o.DedupKey == dedupKey);

    public void Dispose()
    {
        Db.Dispose();
        _connection.Dispose();
    }
}
