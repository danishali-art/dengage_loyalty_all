using dEngage.Loyalty.Schema.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace dEngage.Loyalty.Schema.Configurations;

public class HeldPostingRefundConfiguration : IEntityTypeConfiguration<HeldPostingRefund>
{
    public void Configure(EntityTypeBuilder<HeldPostingRefund> builder)
    {
        builder.ToTable("held_posting_refunds");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.TenantId).HasColumnName("tenant_id");
        builder.Property(x => x.HeldPostingId).HasColumnName("held_posting_id");
        builder.Property(x => x.RefundEventId).HasColumnName("refund_event_id").HasMaxLength(255).IsRequired();
        builder.Property(x => x.Delta).HasColumnName("delta").HasColumnType("numeric(20,4)");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");

        // A redelivered refund event must not take back the same held posting twice.
        builder.HasIndex(x => new { x.TenantId, x.HeldPostingId, x.RefundEventId })
            .IsUnique()
            .HasDatabaseName("ux_held_posting_refunds_tenant_held_refund");
    }
}
