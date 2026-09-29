using dEngage.Loyalty.Schema.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace dEngage.Loyalty.Schema.Configurations;

public class RuleLimitCounterConfiguration : IEntityTypeConfiguration<RuleLimitCounter>
{
    public void Configure(EntityTypeBuilder<RuleLimitCounter> builder)
    {
        builder.ToTable("rule_limit_counters");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.TenantId).HasColumnName("tenant_id");
        builder.Property(x => x.RuleId).HasColumnName("rule_id");
        builder.Property(x => x.CounterType).HasColumnName("counter_type").HasMaxLength(30).IsRequired();
        builder.Property(x => x.PeriodKey).HasColumnName("period_key").HasMaxLength(20).IsRequired();
        builder.Property(x => x.Value).HasColumnName("value").HasColumnType("numeric(20,4)");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");

        builder.HasIndex(x => new { x.TenantId, x.RuleId, x.CounterType, x.PeriodKey })
            .IsUnique()
            .HasDatabaseName("ux_rule_limit_counters_key");
    }
}
