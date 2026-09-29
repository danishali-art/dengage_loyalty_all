using dEngage.Loyalty.Schema.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace dEngage.Loyalty.Schema.Configurations;

public class EventLogConfiguration : IEntityTypeConfiguration<EventLog>
{
    public void Configure(EntityTypeBuilder<EventLog> builder)
    {
        builder.ToTable("event_log");
        builder.HasKey(x => new { x.TenantId, x.EventId });
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").HasMaxLength(100).IsRequired();
        builder.Property(x => x.EventId).HasColumnName("event_id").HasMaxLength(255).IsRequired();
        builder.Property(x => x.ContactKey).HasColumnName("contact_key").HasMaxLength(255).IsRequired();
        builder.Property(x => x.EventType).HasColumnName("event_type").HasMaxLength(50).IsRequired();
        builder.Property(x => x.OccurredAt).HasColumnName("occurred_at");
    }
}
