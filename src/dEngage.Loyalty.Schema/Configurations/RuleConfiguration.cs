using dEngage.Loyalty.Schema.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace dEngage.Loyalty.Schema.Configurations;

public class RuleConfiguration : IEntityTypeConfiguration<Rule>
{
    public void Configure(EntityTypeBuilder<Rule> builder)
    {
        builder.ToTable("rules");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.TenantId).HasColumnName("tenant_id");
        builder.Property(x => x.ProgramId).HasColumnName("program_id");

        builder.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId);
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(255).IsRequired();
        builder.Property(x => x.Type).HasColumnName("type").HasMaxLength(50).IsRequired();
        builder.Property(x => x.Trigger).HasColumnName("trigger").HasMaxLength(50).IsRequired();
        builder.Property(x => x.Conditions).HasColumnName("conditions").HasColumnType("jsonb");
        builder.Property(x => x.Calculation).HasColumnName("calculation").HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.Configuration).HasColumnName("configuration").HasColumnType("jsonb");
        builder.Property(x => x.CurrentVersion).HasColumnName("current_version").IsRequired();
        builder.Property(x => x.TargetAccountTypeId).HasColumnName("target_account_type_id");
        builder.Property(x => x.Limits).HasColumnName("limits").HasColumnType("jsonb");
        builder.Property(x => x.Priority).HasColumnName("priority");
        builder.Property(x => x.Stackable).HasColumnName("stackable");
        builder.Property(x => x.ExclusivityGroup).HasColumnName("exclusivity_group").HasMaxLength(100);
        builder.Property(x => x.StackMode).HasColumnName("stack_mode").HasMaxLength(20).IsRequired();
        builder.Property(x => x.ActiveFrom).HasColumnName("active_from");
        builder.Property(x => x.ActiveTo).HasColumnName("active_to");
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        builder.Property(x => x.Template).HasColumnName("template").HasMaxLength(50);
        builder.Property(x => x.CreatedBy).HasColumnName("created_by").HasMaxLength(100);
        builder.Property(x => x.ApprovedBy).HasColumnName("approved_by").HasMaxLength(100);
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");

        builder.HasOne(x => x.Program)
            .WithMany(x => x.Rules)
            .HasForeignKey(x => x.ProgramId);

        builder.HasOne(x => x.TargetAccountType)
            .WithMany()
            .HasForeignKey(x => x.TargetAccountTypeId)
            .IsRequired(false);

        builder.HasIndex(x => new { x.TenantId, x.ProgramId, x.Status })
            .HasDatabaseName("idx_rules_tenant_program_status");
    }
}
