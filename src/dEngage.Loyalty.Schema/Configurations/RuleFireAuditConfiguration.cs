using dEngage.Loyalty.Schema.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace dEngage.Loyalty.Schema.Configurations;

public class RuleFireAuditConfiguration : IEntityTypeConfiguration<RuleFireAudit>
{
    public void Configure(EntityTypeBuilder<RuleFireAudit> builder)
    {
        builder.ToTable("rule_fire_audit");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.TenantId).HasColumnName("tenant_id");
        builder.Property(x => x.RuleId).HasColumnName("rule_id");

        builder.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId);
        builder.Property(x => x.RuleVersion).HasColumnName("rule_version");
        builder.Property(x => x.SourceEventId).HasColumnName("source_event_id").HasMaxLength(255).IsRequired();
        builder.Property(x => x.ContactKey).HasColumnName("contact_key").HasMaxLength(255).IsRequired();
        builder.Property(x => x.ConditionsSnapshot).HasColumnName("conditions_snapshot").HasColumnType("jsonb");
        builder.Property(x => x.CalculationSnapshot).HasColumnName("calculation_snapshot").HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.ResultingDelta).HasColumnName("resulting_delta").HasColumnType("numeric(20,4)");
        builder.Property(x => x.LedgerEntryId).HasColumnName("ledger_entry_id");
        builder.Property(x => x.ResolutionSnapshot).HasColumnName("resolution_snapshot").HasColumnType("jsonb");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");

        // Loose references (no DB-level FK), matching StreakLog's convention for audit
        // tables — LedgerEntry in particular has a composite (tenant_id, id) primary key
        // a single-column FK can't target cleanly.
        builder.HasIndex(x => new { x.TenantId, x.RuleId, x.CreatedAt })
            .HasDatabaseName("idx_rule_fire_audit_tenant_rule_date");

        // Double-write guard, backing RuleFireAuditWriter's idempotency check — one audit
        // row per (rule, event), matching StreakLog's ux_streak_log_completion pattern.
        builder.HasIndex(x => new { x.TenantId, x.SourceEventId, x.RuleId })
            .IsUnique()
            .HasDatabaseName("ux_rule_fire_audit_source_event_rule");

        // CR 2026-10-02 (Customer 360): per-customer lookups from the customer view.
        builder.HasIndex(x => new { x.TenantId, x.ContactKey, x.CreatedAt })
            .HasDatabaseName("idx_rule_fire_audit_tenant_contact_date");
    }
}
