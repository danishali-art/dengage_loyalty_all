using dEngage.Loyalty.Schema.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace dEngage.Loyalty.Schema.Configurations;

public class RewardDefinitionConfiguration : IEntityTypeConfiguration<RewardDefinition>
{
    public void Configure(EntityTypeBuilder<RewardDefinition> builder)
    {
        builder.ToTable("reward_definitions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.TenantId).HasColumnName("tenant_id");
        builder.Property(x => x.ProgramId).HasColumnName("program_id");

        builder.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId);
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
        builder.Property(x => x.DisplayName).HasColumnName("display_name").HasMaxLength(255).IsRequired();
        builder.Property(x => x.Acquisition).HasColumnName("acquisition").HasMaxLength(30).IsRequired();
        builder.Property(x => x.RewardType).HasColumnName("reward_type").HasMaxLength(30).IsRequired();
        builder.Property(x => x.StampAccountTypeId).HasColumnName("stamp_account_type_id");
        builder.Property(x => x.PointsPrice).HasColumnName("points_price").HasColumnType("numeric(20,4)");
        builder.Property(x => x.PointsAccountTypeId).HasColumnName("points_account_type_id");
        builder.Property(x => x.TypeConfig).HasColumnName("type_config").HasColumnType("jsonb").HasDefaultValue("{}").IsRequired();
        builder.Property(x => x.IsActive).HasColumnName("is_active").HasDefaultValue(true);
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");

        builder.HasOne(x => x.Program)
            .WithMany()
            .HasForeignKey(x => x.ProgramId);

        builder.HasOne(x => x.StampAccountType)
            .WithMany()
            .HasForeignKey(x => x.StampAccountTypeId);

        builder.HasOne(x => x.PointsAccountType)
            .WithMany()
            .HasForeignKey(x => x.PointsAccountTypeId);

        builder.HasIndex(x => new { x.TenantId, x.ProgramId })
            .HasDatabaseName("idx_reward_definitions_tenant_program");

        builder.HasIndex(x => new { x.TenantId, x.ProgramId, x.Name })
            .IsUnique()
            .HasDatabaseName("uq_reward_definitions_tenant_program_name");

        // Only one active stamp reward can be bound to a STAMP account at a time (last line of defense at the DB level)
        builder.HasIndex(x => new { x.TenantId, x.StampAccountTypeId })
            .IsUnique()
            .HasFilter("acquisition = 'stamp_completion' AND is_active")
            .HasDatabaseName("ux_reward_definitions_active_stamp");
    }
}
