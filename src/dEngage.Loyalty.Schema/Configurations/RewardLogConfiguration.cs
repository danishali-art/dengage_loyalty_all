using dEngage.Loyalty.Schema.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace dEngage.Loyalty.Schema.Configurations;

public class RewardLogConfiguration : IEntityTypeConfiguration<RewardLog>
{
    public void Configure(EntityTypeBuilder<RewardLog> builder)
    {
        builder.ToTable("reward_log");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.TenantId).HasColumnName("tenant_id");
        builder.Property(x => x.ContactKey).HasColumnName("contact_key").HasMaxLength(255).IsRequired();

        builder.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId);
        builder.Property(x => x.AccountTypeId).HasColumnName("account_type_id");
        builder.Property(x => x.RewardName).HasColumnName("reward_name").HasMaxLength(100).IsRequired();
        builder.Property(x => x.SourceEventId).HasColumnName("source_event_id").HasMaxLength(255).IsRequired();
        builder.Property(x => x.LedgerResetEntryId).HasColumnName("ledger_reset_entry_id");
        builder.Property(x => x.CompletionCount).HasColumnName("completion_count");
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        builder.Property(x => x.RewardDefinitionId).HasColumnName("reward_definition_id");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.DeliveredAt).HasColumnName("delivered_at");

        builder.HasOne(x => x.AccountType)
            .WithMany()
            .HasForeignKey(x => x.AccountTypeId);

        builder.HasOne(x => x.RewardDefinition)
            .WithMany()
            .HasForeignKey(x => x.RewardDefinitionId);

        // Because ledger_entries is a partitioned table, PostgreSQL does not support an FK reference to it.
        // The LedgerResetEntryId field is kept for application-level joins; there is no DB constraint.

        builder.HasIndex(x => new { x.TenantId, x.ContactKey, x.Status })
            .HasDatabaseName("idx_reward_log_tenant_contact_status");

        // On redelivery, a second reward cannot be written for the same event (last line of defense at the DB level)
        builder.HasIndex(x => new { x.TenantId, x.AccountTypeId, x.SourceEventId })
            .IsUnique()
            .HasDatabaseName("ux_reward_log_source_event");
    }
}
