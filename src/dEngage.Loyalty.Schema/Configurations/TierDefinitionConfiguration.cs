using dEngage.Loyalty.Schema.Entities;
using dEngage.Loyalty.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace dEngage.Loyalty.Schema.Configurations;

public class TierDefinitionConfiguration : IEntityTypeConfiguration<TierDefinition>
{
    public void Configure(EntityTypeBuilder<TierDefinition> builder)
    {
        builder.ToTable("tier_definitions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.TenantId).HasColumnName("tenant_id");
        builder.Property(x => x.ProgramId).HasColumnName("program_id");

        builder.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId);
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
        builder.Property(x => x.DisplayName).HasColumnName("display_name").HasMaxLength(255).IsRequired();
        builder.Property(x => x.MinPoints).HasColumnName("min_points").HasColumnType("numeric(20,4)");
        builder.Property(x => x.QualifyingDays).HasColumnName("qualifying_days");
        builder.Property(x => x.GraceDays).HasColumnName("grace_days").HasDefaultValue(0);
        builder.Property(x => x.SortOrder).HasColumnName("sort_order");
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(20).IsRequired().HasDefaultValue(TierStatus.Active);
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");

        builder.HasOne(x => x.Program)
            .WithMany()
            .HasForeignKey(x => x.ProgramId);

        builder.HasIndex(x => new { x.TenantId, x.ProgramId })
            .HasDatabaseName("idx_tier_definitions_tenant_program");

        // Filtered so a soft-deleted tier (status='deleted') doesn't permanently block reuse
        // of its name/sort_order by a future tier in the same program.
        builder.HasIndex(x => new { x.ProgramId, x.Name })
            .IsUnique()
            .HasFilter("status != 'deleted'")
            .HasDatabaseName("uq_tier_definitions_program_name");

        builder.HasIndex(x => new { x.ProgramId, x.SortOrder })
            .IsUnique()
            .HasFilter("status != 'deleted'")
            .HasDatabaseName("uq_tier_definitions_program_sort_order");
    }
}
