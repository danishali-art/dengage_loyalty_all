using dEngage.Loyalty.Schema.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace dEngage.Loyalty.Schema.Configurations;

public class StreakPeriodStateConfiguration : IEntityTypeConfiguration<StreakPeriodState>
{
    public void Configure(EntityTypeBuilder<StreakPeriodState> builder)
    {
        builder.ToTable("streak_period_state");
        builder.HasKey(x => new { x.TenantId, x.CampaignId, x.ContactKey, x.PeriodStart });
        builder.Property(x => x.TenantId).HasColumnName("tenant_id");
        builder.Property(x => x.CampaignId).HasColumnName("campaign_id");

        builder.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId);
        builder.Property(x => x.ContactKey).HasColumnName("contact_key").HasMaxLength(255).IsRequired();
        builder.Property(x => x.PeriodStart).HasColumnName("period_start");
        builder.Property(x => x.AggSum).HasColumnName("agg_sum").HasColumnType("numeric(18,2)");
        builder.Property(x => x.AggCount).HasColumnName("agg_count");
        builder.Property(x => x.Met).HasColumnName("met");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");

        // Nightly break detection scans by period
        builder.HasIndex(x => new { x.TenantId, x.CampaignId, x.PeriodStart })
            .HasDatabaseName("idx_streak_period_state_rule_period");
    }
}
