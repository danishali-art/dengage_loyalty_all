using dEngage.Loyalty.Schema.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace dEngage.Loyalty.Schema.Configurations;

public class TenantApiKeyConfiguration : IEntityTypeConfiguration<TenantApiKey>
{
    public void Configure(EntityTypeBuilder<TenantApiKey> builder)
    {
        builder.ToTable("tenant_api_keys");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        // ApiKeyGenerator emits "lk_{tenantId}_{8 chars}" — tenant ids run up to 50 chars
        // (see CreateTenantRequestValidator), so 20 is not enough; 80 covers the worst case
        // with headroom.
        builder.Property(x => x.KeyPrefix).HasColumnName("key_prefix").HasMaxLength(80).IsRequired();
        builder.Property(x => x.HashedKey).HasColumnName("hashed_key").HasMaxLength(255).IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.LastUsedAt).HasColumnName("last_used_at");
        builder.Property(x => x.RevokedAt).HasColumnName("revoked_at");

        builder.HasOne(x => x.Tenant)
            .WithMany()
            .HasForeignKey(x => x.TenantId);

        builder.HasIndex(x => x.TenantId).HasDatabaseName("idx_tenant_api_keys_tenant_id");
        builder.HasIndex(x => x.KeyPrefix).IsUnique().HasDatabaseName("uq_tenant_api_keys_prefix");
    }
}
