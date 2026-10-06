using dEngage.Loyalty.Schema.Entities;
using Microsoft.EntityFrameworkCore;

namespace dEngage.Loyalty.Schema;

public class LoyaltyDbContext(DbContextOptions<LoyaltyDbContext> options) : DbContext(options)
{
    public DbSet<Entities.Program> Programs => Set<Entities.Program>();
    public DbSet<AccountType> AccountTypes => Set<AccountType>();
    public DbSet<Rule> Rules => Set<Rule>();
    public DbSet<StreakCampaign> StreakCampaigns => Set<StreakCampaign>();
    public DbSet<CustomerAccount> CustomerAccounts => Set<CustomerAccount>();
    public DbSet<LedgerEntry> LedgerEntries => Set<LedgerEntry>();
    public DbSet<RuleFireAudit> RuleFireAudits => Set<RuleFireAudit>();
    public DbSet<HeldPosting> HeldPostings => Set<HeldPosting>();
    public DbSet<RuleVersion> RuleVersions => Set<RuleVersion>();
    public DbSet<RuleLimitCounter> RuleLimitCounters => Set<RuleLimitCounter>();
    public DbSet<EventInbox> EventInbox => Set<EventInbox>();
    public DbSet<EventLog> EventLog => Set<EventLog>();
    public DbSet<RewardLog> RewardLogs => Set<RewardLog>();
    public DbSet<RewardDefinition> RewardDefinitions => Set<RewardDefinition>();
    public DbSet<TierDefinition> TierDefinitions => Set<TierDefinition>();
    public DbSet<TierUpgradeLog> TierUpgradeLogs => Set<TierUpgradeLog>();
    public DbSet<OutboxEvent> OutboxEvents => Set<OutboxEvent>();
    public DbSet<StreakAppliedEvent> StreakAppliedEvents => Set<StreakAppliedEvent>();
    public DbSet<StreakPeriodState> StreakPeriodStates => Set<StreakPeriodState>();
    public DbSet<StreakProgress> StreakProgresses => Set<StreakProgress>();
    public DbSet<StreakLog> StreakLogs => Set<StreakLog>();
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<TenantApiKey> TenantApiKeys => Set<TenantApiKey>();
    public DbSet<AdminUser> AdminUsers => Set<AdminUser>();
    public DbSet<ConfigVersion> ConfigVersions => Set<ConfigVersion>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(LoyaltyDbContext).Assembly);
    }
}
