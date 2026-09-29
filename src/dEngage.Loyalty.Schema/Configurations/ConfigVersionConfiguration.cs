using dEngage.Loyalty.Schema.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace dEngage.Loyalty.Schema.Configurations;

public class ConfigVersionConfiguration : IEntityTypeConfiguration<ConfigVersion>
{
    public void Configure(EntityTypeBuilder<ConfigVersion> builder)
    {
        builder.ToTable("config_versions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.TenantId).HasColumnName("tenant_id");
        builder.Property(x => x.EntityType).HasColumnName("entity_type").HasMaxLength(64).IsRequired();
        builder.Property(x => x.EntityId).HasColumnName("entity_id");
        builder.Property(x => x.VersionNumber).HasColumnName("version_number");
        builder.Property(x => x.Snapshot).HasColumnName("snapshot").HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.ChangeType).HasColumnName("change_type").HasMaxLength(20).IsRequired();
        builder.Property(x => x.ChangeSummary).HasColumnName("change_summary");
        builder.Property(x => x.ChangedBy).HasColumnName("changed_by").HasMaxLength(255).IsRequired();
        builder.Property(x => x.ChangedAt).HasColumnName("changed_at");

        builder.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId);

        builder.HasIndex(x => new { x.TenantId, x.EntityType, x.EntityId, x.VersionNumber })
            .IsUnique()
            .HasDatabaseName("ux_config_versions_entity_version");

        builder.HasIndex(x => new { x.TenantId, x.EntityType, x.EntityId, x.ChangedAt })
            .HasDatabaseName("idx_config_versions_entity_timeline");
    }
}
