using dEngage.Loyalty.Schema.Entities;
using dEngage.Loyalty.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace dEngage.Loyalty.Schema.Configurations;

public class ComplaintConfiguration : IEntityTypeConfiguration<Complaint>
{
    public void Configure(EntityTypeBuilder<Complaint> builder)
    {
        builder.ToTable("complaints");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.TenantId).HasColumnName("tenant_id");
        builder.Property(x => x.ProgramId).HasColumnName("program_id");
        builder.Property(x => x.CustomerKey).HasColumnName("customer_key").HasMaxLength(255);
        builder.Property(x => x.Subject).HasColumnName("subject").HasMaxLength(255).IsRequired();
        builder.Property(x => x.Description).HasColumnName("description");
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(20).IsRequired().HasDefaultValue(ComplaintStatus.Open);
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.ResolvedAt).HasColumnName("resolved_at");

        builder.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId);

        builder.HasOne(x => x.Program)
            .WithMany()
            .HasForeignKey(x => x.ProgramId)
            .IsRequired(false);

        builder.HasIndex(x => new { x.TenantId, x.Status })
            .HasDatabaseName("idx_complaints_tenant_status");

        builder.HasIndex(x => new { x.TenantId, x.ProgramId })
            .HasDatabaseName("idx_complaints_tenant_program");
    }
}
