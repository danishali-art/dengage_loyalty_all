using dEngage.Loyalty.Schema.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace dEngage.Loyalty.Schema.Configurations;

public class CustomerBirthdayConfiguration : IEntityTypeConfiguration<CustomerBirthday>
{
    public void Configure(EntityTypeBuilder<CustomerBirthday> builder)
    {
        builder.ToTable("customer_birthdays");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.TenantId).HasColumnName("tenant_id");
        builder.Property(x => x.ContactKey).HasColumnName("contact_key").HasMaxLength(255).IsRequired();
        builder.Property(x => x.MonthDay).HasColumnName("month_day").HasMaxLength(5).IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");

        builder.HasIndex(x => new { x.TenantId, x.ContactKey })
            .IsUnique()
            .HasDatabaseName("ux_customer_birthdays_tenant_contact");

        // Drives BirthdayBonusJob's daily sweep: every customer whose MonthDay matches today.
        builder.HasIndex(x => new { x.TenantId, x.MonthDay })
            .HasDatabaseName("idx_customer_birthdays_month_day");
    }
}
