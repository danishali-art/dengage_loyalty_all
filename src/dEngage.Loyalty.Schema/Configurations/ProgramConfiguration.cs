using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace dEngage.Loyalty.Schema.Configurations;

public class ProgramConfiguration : IEntityTypeConfiguration<Entities.Program>
{
    public void Configure(EntityTypeBuilder<Entities.Program> builder)
    {
        builder.ToTable("programs");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.TenantId).HasColumnName("tenant_id");
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(255).IsRequired();

        builder.HasOne<Entities.Tenant>().WithMany().HasForeignKey(x => x.TenantId);
        builder.Property(x => x.Description).HasColumnName("description");
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.QualifyingAccountTypeId).HasColumnName("qualifying_account_type_id");
        builder.Property(x => x.WarningDays).HasColumnName("warning_days");
        builder.Property(x => x.PublicationStatus).HasColumnName("publication_status").HasMaxLength(20).IsRequired();
        builder.Property(x => x.HasUnpublishedChanges).HasColumnName("has_unpublished_changes");
        builder.Property(x => x.PublishedVersion).HasColumnName("published_version");
        builder.Property(x => x.PublishedAt).HasColumnName("published_at");
        builder.Property(x => x.PublishedBy).HasColumnName("published_by").HasMaxLength(255);

        builder.HasOne(x => x.QualifyingAccountType)
            .WithMany()
            .HasForeignKey(x => x.QualifyingAccountTypeId)
            .IsRequired(false);

        builder.HasIndex(x => x.TenantId).HasDatabaseName("idx_programs_tenant_id");
    }
}
