using dEngage.Loyalty.Schema.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace dEngage.Loyalty.Schema.Configurations;

public class LoyaltyConfigEntryConfiguration : IEntityTypeConfiguration<LoyaltyConfigEntry>
{
    public void Configure(EntityTypeBuilder<LoyaltyConfigEntry> builder)
    {
        builder.ToTable("loyalty_config");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseIdentityByDefaultColumn();
        builder.Property(x => x.ModuleName).HasColumnName("module_name").HasMaxLength(100).IsRequired();
        builder.Property(x => x.ParameterName).HasColumnName("parameter_name").HasMaxLength(200).IsRequired();
        builder.Property(x => x.TenantId).HasColumnName("tenant_id");
        builder.Property(x => x.Value).HasColumnName("value");
        builder.Property(x => x.ValueJson).HasColumnName("value_json").HasColumnType("jsonb");
        builder.Property(x => x.IsSecret).HasColumnName("is_secret").HasDefaultValue(false);
        builder.Property(x => x.Description).HasColumnName("description").HasMaxLength(500);
        // CURRENT_TIMESTAMP rather than now(): the same default must also work on Sqlite (tests).
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(x => x.UpdatedBy).HasColumnName("updated_by").HasMaxLength(255);

        builder.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId);

        // One global row and at most one row per tenant for each (module, parameter). Two partial
        // indexes instead of one, because NULL tenant_ids never collide in a plain unique index
        // (and NULLS NOT DISTINCT needs Postgres 15; the dEngage platform still runs 14).
        builder.HasIndex(x => new { x.ModuleName, x.ParameterName })
            .IsUnique()
            .HasFilter("tenant_id IS NULL")
            .HasDatabaseName("ux_loyalty_config_global");

        builder.HasIndex(x => new { x.TenantId, x.ModuleName, x.ParameterName })
            .IsUnique()
            .HasFilter("tenant_id IS NOT NULL")
            .HasDatabaseName("ux_loyalty_config_tenant");
    }
}
