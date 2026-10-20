using dEngage.Loyalty.Schema.Entities;
using dEngage.Loyalty.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace dEngage.Loyalty.Schema.Configurations;

public class LoyaltyTenantConfigEntryConfiguration : IEntityTypeConfiguration<LoyaltyTenantConfigEntry>
{
    public void Configure(EntityTypeBuilder<LoyaltyTenantConfigEntry> builder)
    {
        // Only tenant settings belong here (loyalty_config holds the global ones). Generated from
        // the list, so a change to LoyaltyConfigKeys.TenantSettings shows up as a model change.
        builder.ToTable("loyalty_tenant_config", t => t.HasCheckConstraint(
            "ck_loyalty_tenant_config_tenant_settings",
            IsTenantSettingSql()));

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.TenantId).HasColumnName("tenant_id");
        builder.Property(x => x.ModuleName).HasColumnName("module_name").HasMaxLength(100).IsRequired();
        builder.Property(x => x.ParameterName).HasColumnName("parameter_name").HasMaxLength(200).IsRequired();
        builder.Property(x => x.Value).HasColumnName("value");
        builder.Property(x => x.ValueJson).HasColumnName("value_json").HasColumnType("jsonb");
        builder.Property(x => x.IsSecret).HasColumnName("is_secret").HasDefaultValue(false);
        builder.Property(x => x.Description).HasColumnName("description").HasMaxLength(500);
        // CURRENT_TIMESTAMP rather than now(): the same default must also work on Sqlite (tests).
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(x => x.UpdatedBy).HasColumnName("updated_by").HasMaxLength(255);

        // Deleting a tenant removes its settings.
        builder.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId);

        // One row per setting per tenant.
        builder.HasIndex(x => new { x.TenantId, x.ModuleName, x.ParameterName })
            .IsUnique()
            .HasDatabaseName("ux_loyalty_tenant_config_key");
    }

    // "(module_name = 'loyalty.api' AND parameter_name = '...') OR ..." — plain comparisons rather
    // than a row-value IN list, which Sqlite (the API tests' database) doesn't allow in a CHECK.
    internal static string IsTenantSettingSql() =>
        string.Join(" OR ", LoyaltyConfigKeys.TenantSettings.Select(k =>
            $"(module_name = {Literal(k.Module)} AND parameter_name = {Literal(k.Parameter)})"));

    private static string Literal(string value) => "'" + value.Replace("'", "''") + "'";
}
