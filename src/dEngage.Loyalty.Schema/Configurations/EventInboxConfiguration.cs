using dEngage.Loyalty.Schema.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace dEngage.Loyalty.Schema.Configurations;

public class EventInboxConfiguration : IEntityTypeConfiguration<EventInbox>
{
    public void Configure(EntityTypeBuilder<EventInbox> builder)
    {
        builder.ToTable("event_inbox");
        builder.HasKey(x => new { x.TenantId, x.EventId });
        builder.Property(x => x.EventId).HasColumnName("event_id").HasMaxLength(255).IsRequired();
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").HasMaxLength(100).IsRequired();
        builder.Property(x => x.EventType).HasColumnName("event_type").HasMaxLength(50).IsRequired();
        builder.Property(x => x.Payload).HasColumnName("payload").HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.ReceivedAt).HasColumnName("received_at");
        builder.Property(x => x.ProcessedAt).HasColumnName("processed_at");
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        builder.Property(x => x.Error).HasColumnName("error");
        builder.Property(x => x.ContactKey).HasColumnName("contact_key").HasMaxLength(255);

        // CR 2026-10-02 (Customer 360): the customer view's Events tab. Postgres adds it to
        // every per-tenant partition, existing and future.
        builder.HasIndex(x => new { x.TenantId, x.ContactKey, x.ReceivedAt })
            .HasDatabaseName("idx_event_inbox_tenant_contact_received");
    }
}
