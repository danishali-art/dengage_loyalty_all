using dEngage.Loyalty.Schema.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace dEngage.Loyalty.Schema.Configurations;

public class RuleVersionConfiguration : IEntityTypeConfiguration<RuleVersion>
{
    public void Configure(EntityTypeBuilder<RuleVersion> builder)
    {
        builder.ToTable("rule_versions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.TenantId).HasColumnName("tenant_id");
        builder.Property(x => x.RuleId).HasColumnName("rule_id");
        builder.Property(x => x.VersionNumber).HasColumnName("version_number");
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(255).IsRequired();
        builder.Property(x => x.Type).HasColumnName("type").HasMaxLength(50).IsRequired();
        builder.Property(x => x.Trigger).HasColumnName("trigger").HasMaxLength(100).IsRequired();
        builder.Property(x => x.Conditions).HasColumnName("conditions").HasColumnType("jsonb");
        builder.Property(x => x.Calculation).HasColumnName("calculation").HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.TargetAccountTypeId).HasColumnName("target_account_type_id");
        builder.Property(x => x.Limits).HasColumnName("limits").HasColumnType("jsonb");
        builder.Property(x => x.Configuration).HasColumnName("configuration").HasColumnType("jsonb");
        builder.Property(x => x.Priority).HasColumnName("priority");
        builder.Property(x => x.Stackable).HasColumnName("stackable");
        builder.Property(x => x.ExclusivityGroup).HasColumnName("exclusivity_group").HasMaxLength(100);
        builder.Property(x => x.StackMode).HasColumnName("stack_mode").HasMaxLength(20).IsRequired();
        builder.Property(x => x.ActiveFrom).HasColumnName("active_from");
        builder.Property(x => x.ActiveTo).HasColumnName("active_to");
        builder.Property(x => x.EffectiveFrom).HasColumnName("effective_from");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");

        builder.HasIndex(x => new { x.TenantId, x.RuleId, x.VersionNumber })
            .IsUnique()
            .HasDatabaseName("ux_rule_versions_tenant_rule_version");
    }
}
