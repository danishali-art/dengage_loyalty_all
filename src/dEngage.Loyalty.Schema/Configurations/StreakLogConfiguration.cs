using dEngage.Loyalty.Schema.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace dEngage.Loyalty.Schema.Configurations;

public class StreakLogConfiguration : IEntityTypeConfiguration<StreakLog>
{
    public void Configure(EntityTypeBuilder<StreakLog> builder)
    {
        builder.ToTable("streak_log");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.TenantId).HasColumnName("tenant_id");
        builder.Property(x => x.CampaignId).HasColumnName("campaign_id");

        builder.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId);
        builder.Property(x => x.ContactKey).HasColumnName("contact_key").HasMaxLength(255).IsRequired();
        builder.Property(x => x.CompletionNo).HasColumnName("completion_no");
        builder.Property(x => x.CompletedPeriod).HasColumnName("completed_period");
        builder.Property(x => x.Periods).HasColumnName("periods");
        builder.Property(x => x.RewardKind).HasColumnName("reward_kind").HasMaxLength(50).IsRequired();
        builder.Property(x => x.RewardRef).HasColumnName("reward_ref").HasMaxLength(255);
        builder.Property(x => x.SourceEventId).HasColumnName("source_event_id").HasMaxLength(255).IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");

        // Double-reward guard: one row per completion
        builder.HasIndex(x => new { x.TenantId, x.CampaignId, x.ContactKey, x.CompletionNo })
            .IsUnique()
            .HasDatabaseName("ux_streak_log_completion");

        // CR 2026-10-02 (Customer 360): per-customer lookups from the customer view.
        builder.HasIndex(x => new { x.TenantId, x.ContactKey })
            .HasDatabaseName("idx_streak_log_tenant_contact");
    }
}
