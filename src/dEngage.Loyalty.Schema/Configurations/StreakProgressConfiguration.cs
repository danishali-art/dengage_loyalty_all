using dEngage.Loyalty.Schema.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace dEngage.Loyalty.Schema.Configurations;

public class StreakProgressConfiguration : IEntityTypeConfiguration<StreakProgress>
{
    public void Configure(EntityTypeBuilder<StreakProgress> builder)
    {
        builder.ToTable("streak_progress");
        builder.HasKey(x => new { x.TenantId, x.CampaignId, x.ContactKey });
        builder.Property(x => x.TenantId).HasColumnName("tenant_id");
        builder.Property(x => x.CampaignId).HasColumnName("campaign_id");

        builder.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId);
        builder.Property(x => x.ContactKey).HasColumnName("contact_key").HasMaxLength(255).IsRequired();
        builder.Property(x => x.StreakCount).HasColumnName("streak_count");
        builder.Property(x => x.LastMetPeriod).HasColumnName("last_met_period");
        builder.Property(x => x.Completions).HasColumnName("completions");
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");
    }
}
