using dEngage.Loyalty.Schema.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace dEngage.Loyalty.Schema.Configurations;

public class LedgerEntryConfiguration : IEntityTypeConfiguration<LedgerEntry>
{
    public void Configure(EntityTypeBuilder<LedgerEntry> builder)
    {
        builder.ToTable("ledger_entries");
        builder.HasKey(x => new { x.TenantId, x.Id });
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").HasMaxLength(100).IsRequired();
        builder.Property(x => x.CustomerAccountId).HasColumnName("customer_account_id");
        builder.Property(x => x.ContactKey).HasColumnName("contact_key").HasMaxLength(255).IsRequired();
        builder.Property(x => x.Delta).HasColumnName("delta").HasColumnType("numeric(20,4)");
        builder.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(50).IsRequired();
        builder.Property(x => x.SourceEventId).HasColumnName("source_event_id").HasMaxLength(255).IsRequired();
        builder.Property(x => x.RuleId).HasColumnName("rule_id");
        builder.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(500).IsRequired();
        builder.Property(x => x.Metadata).HasColumnName("metadata").HasColumnType("jsonb");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.ExpiresAt).HasColumnName("expires_at"); // CR 2026-10-06 Phase 5

        builder.HasOne(x => x.CustomerAccount)
            .WithMany(x => x.LedgerEntries)
            .HasForeignKey(x => x.CustomerAccountId)
            .HasPrincipalKey(x => x.Id);

        builder.HasOne(x => x.Rule)
            .WithMany()
            .HasForeignKey(x => x.RuleId);

        builder.HasIndex(x => new { x.TenantId, x.IdempotencyKey })
            .IsUnique()
            .HasDatabaseName("uq_ledger_entries_idempotency_key");

        builder.HasIndex(x => new { x.TenantId, x.CustomerAccountId, x.CreatedAt })
            .HasDatabaseName("idx_ledger_entries_tenant_account_date");

        builder.HasIndex(x => new { x.TenantId, x.SourceEventId })
            .HasDatabaseName("idx_ledger_entries_tenant_source_event");
    }
}
