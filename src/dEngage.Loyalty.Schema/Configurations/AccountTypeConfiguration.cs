using dEngage.Loyalty.Schema.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace dEngage.Loyalty.Schema.Configurations;

public class AccountTypeConfiguration : IEntityTypeConfiguration<AccountType>
{
    public void Configure(EntityTypeBuilder<AccountType> builder)
    {
        builder.ToTable("account_types");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.TenantId).HasColumnName("tenant_id");
        builder.Property(x => x.ProgramId).HasColumnName("program_id");

        builder.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId);
        builder.Property(x => x.Type).HasColumnName("type").HasMaxLength(20).IsRequired();
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(255).IsRequired();
        builder.Property(x => x.Config).HasColumnName("config").HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.IsTierQualifying).HasColumnName("is_tier_qualifying");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");

        builder.HasOne(x => x.Program)
            .WithMany(x => x.AccountTypes)
            .HasForeignKey(x => x.ProgramId);

        builder.HasIndex(x => new { x.TenantId, x.ProgramId }).HasDatabaseName("idx_account_types_tenant_program");

        // At most one tier-qualifying wallet per program (1.3.CL item 1).
        // Named overload: an unnamed HasIndex on the same columns would merge into the index above.
        builder.HasIndex(x => new { x.TenantId, x.ProgramId }, "ux_account_types_tier_qualifying")
            .IsUnique()
            .HasFilter("is_tier_qualifying");
    }
}
