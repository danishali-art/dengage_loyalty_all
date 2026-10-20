using dEngage.Loyalty.Schema.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace dEngage.Loyalty.Schema.Configurations;

public class LoyaltyConfigEntryConfiguration : IEntityTypeConfiguration<LoyaltyConfigEntry>
{
    public void Configure(EntityTypeBuilder<LoyaltyConfigEntry> builder)
    {
        // Tenant settings never have a global row; they live in loyalty_tenant_config only.
        builder.ToTable("loyalty_config", t => t.HasCheckConstraint(
            "ck_loyalty_config_global_settings",
            $"NOT ({LoyaltyTenantConfigEntryConfiguration.IsTenantSettingSql()})"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseIdentityByDefaultColumn();
        builder.Property(x => x.ModuleName).HasColumnName("module_name").HasMaxLength(100).IsRequired();
        builder.Property(x => x.ParameterName).HasColumnName("parameter_name").HasMaxLength(200).IsRequired();
        builder.Property(x => x.Value).HasColumnName("value");
        builder.Property(x => x.ValueJson).HasColumnName("value_json").HasColumnType("jsonb");
        builder.Property(x => x.IsSecret).HasColumnName("is_secret").HasDefaultValue(false);
        builder.Property(x => x.Description).HasColumnName("description").HasMaxLength(500);
        // CURRENT_TIMESTAMP rather than now(): the same default must also work on Sqlite (tests).
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(x => x.UpdatedBy).HasColumnName("updated_by").HasMaxLength(255);

        // Global settings only: exactly one row per (module, parameter).
        builder.HasIndex(x => new { x.ModuleName, x.ParameterName })
            .IsUnique()
            .HasDatabaseName("ux_loyalty_config_key");
    }
}
