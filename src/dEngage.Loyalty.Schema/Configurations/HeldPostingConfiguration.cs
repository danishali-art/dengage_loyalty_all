using dEngage.Loyalty.Schema.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace dEngage.Loyalty.Schema.Configurations;

public class HeldPostingConfiguration : IEntityTypeConfiguration<HeldPosting>
{
    public void Configure(EntityTypeBuilder<HeldPosting> builder)
    {
        builder.ToTable("held_postings");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.TenantId).HasColumnName("tenant_id");
        builder.Property(x => x.RuleId).HasColumnName("rule_id");
        builder.Property(x => x.CustomerAccountId).HasColumnName("customer_account_id");
        builder.Property(x => x.ContactKey).HasColumnName("contact_key").HasMaxLength(255).IsRequired();
        builder.Property(x => x.Delta).HasColumnName("delta").HasColumnType("numeric(20,4)");
        builder.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(50).IsRequired();
        builder.Property(x => x.SourceEventId).HasColumnName("source_event_id").HasMaxLength(255).IsRequired();
        builder.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(255).IsRequired();
        builder.Property(x => x.Metadata).HasColumnName("metadata").HasColumnType("jsonb");
        builder.Property(x => x.HoldUntil).HasColumnName("hold_until");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.PostedAt).HasColumnName("posted_at");
        builder.Property(x => x.LedgerEntryId).HasColumnName("ledger_entry_id");

        builder.HasIndex(x => new { x.TenantId, x.IdempotencyKey })
            .IsUnique()
            .HasDatabaseName("ux_held_postings_tenant_idempotency");

        // Drives DelayedPostingPromotionJob's sweep: unposted rows due by HoldUntil.
        builder.HasIndex(x => new { x.HoldUntil, x.PostedAt })
            .HasDatabaseName("idx_held_postings_due");
    }
}
