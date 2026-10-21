using dEngage.Loyalty.Schema.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace dEngage.Loyalty.Schema.Configurations;

public class TierUpgradeLogConfiguration : IEntityTypeConfiguration<TierUpgradeLog>
{
    public void Configure(EntityTypeBuilder<TierUpgradeLog> builder)
    {
        builder.ToTable("tier_upgrade_log");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.TenantId).HasColumnName("tenant_id");
        builder.Property(x => x.ContactKey).HasColumnName("contact_key").HasMaxLength(255).IsRequired();

        builder.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId);
        builder.Property(x => x.FromTierId).HasColumnName("from_tier_id");
        builder.Property(x => x.ToTierId).HasColumnName("to_tier_id");
        builder.Property(x => x.QualifyingPts).HasColumnName("qualifying_pts").HasColumnType("numeric(20,4)");
        builder.Property(x => x.SourceEventId).HasColumnName("source_event_id").HasMaxLength(255).IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");

        builder.HasOne(x => x.FromTier)
            .WithMany()
            .HasForeignKey(x => x.FromTierId)
            .IsRequired(false);

        builder.HasOne(x => x.ToTier)
            .WithMany()
            .HasForeignKey(x => x.ToTierId);

        builder.HasIndex(x => new { x.TenantId, x.ContactKey })
            .HasDatabaseName("idx_tier_upgrade_log_tenant_contact");

        // Race guard: a single tier change log per event and target tier.
        // Keeps TierEvaluationService from writing double logs on concurrent events. The target
        // tier is part of the key because one event runs every active program, and each program
        // has its own tiers, so one event can change the tier in several programs.
        builder.HasIndex(x => new { x.TenantId, x.ContactKey, x.SourceEventId, x.ToTierId })
            .IsUnique()
            .HasDatabaseName("ux_tier_upgrade_log_source_event");
    }
}
