using dEngage.Loyalty.Schema.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace dEngage.Loyalty.Schema.Configurations;

public class OutboxEventConfiguration : IEntityTypeConfiguration<OutboxEvent>
{
    public void Configure(EntityTypeBuilder<OutboxEvent> builder)
    {
        builder.ToTable("outbox_events");
        builder.HasKey(x => x.Id);

        // bigint identity: id order = enqueue order = publish order
        builder.Property(x => x.Id).HasColumnName("id").UseIdentityByDefaultColumn();
        builder.Property(x => x.EventId).HasColumnName("event_id");
        builder.Property(x => x.TenantId).HasColumnName("tenant_id");
        builder.Property(x => x.EventType).HasColumnName("event_type").HasMaxLength(100).IsRequired();

        builder.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId);
        builder.Property(x => x.ContactKey).HasColumnName("contact_key").HasMaxLength(255).IsRequired();
        builder.Property(x => x.Payload).HasColumnName("payload").HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.DedupKey).HasColumnName("dedup_key").HasMaxLength(255);
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        builder.Property(x => x.Attempts).HasColumnName("attempts");
        builder.Property(x => x.NextAttemptAt).HasColumnName("next_attempt_at");
        builder.Property(x => x.PublishedAt).HasColumnName("published_at");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");

        builder.HasIndex(x => x.EventId)
            .IsUnique()
            .HasDatabaseName("ux_outbox_event_id");

        // The publisher poll scans only pending rows
        builder.HasIndex(x => x.NextAttemptAt)
            .HasFilter("status = 'pending'")
            .HasDatabaseName("idx_outbox_pending_next_attempt");

        // On redelivery, a second outbox row cannot be written for the same business event
        builder.HasIndex(x => new { x.TenantId, x.DedupKey })
            .IsUnique()
            .HasFilter("dedup_key IS NOT NULL")
            .HasDatabaseName("ux_outbox_dedup");

        // CR 2026-10-02 (Customer 360): per-customer lookups from the customer view.
        builder.HasIndex(x => new { x.TenantId, x.ContactKey, x.CreatedAt })
            .HasDatabaseName("idx_outbox_tenant_contact_date");
    }
}
