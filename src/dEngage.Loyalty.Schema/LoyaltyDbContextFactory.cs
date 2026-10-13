using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace dEngage.Loyalty.Schema;

public class LoyaltyDbContextFactory : IDesignTimeDbContextFactory<LoyaltyDbContext>
{
    public LoyaltyDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<LoyaltyDbContext>()
            .UseNpgsql("Host=localhost;Port=5433;Database=loyalty_dev;Username=postgres;Password=postgres")
            .Options;

        return new LoyaltyDbContext(options);
    }
}
