using dEngage.Loyalty.Schema.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace dEngage.Loyalty.Schema.Configurations;

public class StreakAppliedEventConfiguration : IEntityTypeConfiguration<StreakAppliedEvent>
{
    public void Configure(EntityTypeBuilder<StreakAppliedEvent> builder)
    {
        builder.ToTable("streak_applied_event");
        builder.HasKey(x => new { x.TenantId, x.CampaignId, x.EventId });
        builder.Property(x => x.TenantId).HasColumnName("tenant_id");
        builder.Property(x => x.CampaignId).HasColumnName("campaign_id");
        builder.Property(x => x.EventId).HasColumnName("event_id").HasMaxLength(255).IsRequired();
        builder.Property(x => x.AppliedAt).HasColumnName("applied_at");

        builder.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId);
    }
}
