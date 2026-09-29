using dEngage.Loyalty.Schema.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace dEngage.Loyalty.Schema.Configurations;

public class StreakCampaignConfiguration : IEntityTypeConfiguration<StreakCampaign>
{
    public void Configure(EntityTypeBuilder<StreakCampaign> builder)
    {
        builder.ToTable("streak_campaigns");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.TenantId).HasColumnName("tenant_id");
        builder.Property(x => x.ProgramId).HasColumnName("program_id");

        builder.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId);
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(255).IsRequired();
        builder.Property(x => x.Trigger).HasColumnName("trigger").HasMaxLength(50).IsRequired();
        builder.Property(x => x.TargetAccountTypeId).HasColumnName("target_account_type_id");
        builder.Property(x => x.Conditions).HasColumnName("conditions").HasColumnType("jsonb");
        builder.Property(x => x.Config).HasColumnName("config").HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.ActiveFrom).HasColumnName("active_from");
        builder.Property(x => x.ActiveTo).HasColumnName("active_to");
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");

        builder.HasOne(x => x.Program)
            .WithMany()
            .HasForeignKey(x => x.ProgramId);

        builder.HasOne(x => x.TargetAccountType)
            .WithMany()
            .HasForeignKey(x => x.TargetAccountTypeId);

        builder.HasIndex(x => new { x.TenantId, x.ProgramId, x.Status })
            .HasDatabaseName("idx_streak_campaigns_tenant_program_status");
    }
}
