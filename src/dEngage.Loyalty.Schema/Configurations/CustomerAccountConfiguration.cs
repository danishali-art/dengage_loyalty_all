using dEngage.Loyalty.Schema.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace dEngage.Loyalty.Schema.Configurations;

public class CustomerAccountConfiguration : IEntityTypeConfiguration<CustomerAccount>
{
    public void Configure(EntityTypeBuilder<CustomerAccount> builder)
    {
        builder.ToTable("customer_accounts");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.TenantId).HasColumnName("tenant_id");
        builder.Property(x => x.ContactKey).HasColumnName("contact_key").HasMaxLength(255).IsRequired();

        builder.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId);
        builder.Property(x => x.AccountTypeId).HasColumnName("account_type_id");
        builder.Property(x => x.Balance).HasColumnName("balance").HasColumnType("numeric(20,4)");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        builder.Property(x => x.TierId).HasColumnName("tier_id");
        builder.Property(x => x.TierQualifyingPts).HasColumnName("tier_qualifying_pts").HasColumnType("numeric(20,4)").HasDefaultValue(0m);
        builder.Property(x => x.TierPeriodStart).HasColumnName("tier_period_start");
        builder.Property(x => x.TierExpiresAt).HasColumnName("tier_expires_at");

        builder.HasOne(x => x.AccountType)
            .WithMany()
            .HasForeignKey(x => x.AccountTypeId);

        builder.HasOne(x => x.Tier)
            .WithMany()
            .HasForeignKey(x => x.TierId)
            .IsRequired(false);

        builder.HasIndex(x => new { x.TenantId, x.ContactKey, x.AccountTypeId })
            .IsUnique()
            .HasDatabaseName("uq_customer_accounts_tenant_contact_accounttype");

        builder.HasIndex(x => new { x.TenantId, x.ContactKey })
            .HasDatabaseName("idx_customer_accounts_tenant_contact");
    }
}
