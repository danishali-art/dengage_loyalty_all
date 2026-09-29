using dEngage.Loyalty.RuleEngine.Campaigns.Streak;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace dEngage.Loyalty.Consumer;

public class StreakMaintenanceWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<StreakMaintenanceWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        logger.LogInformation("StreakMaintenanceWorker: started");

        while (!ct.IsCancellationRequested)
        {
            var nextRun = NextRunAt(DateTime.UtcNow);
            var delay = nextRun - DateTime.UtcNow;

            logger.LogInformation("StreakMaintenanceWorker: next run at {NextRun:O} (in {Minutes:F0} min)",
                nextRun, delay.TotalMinutes);

            await Task.Delay(delay, ct);

            if (ct.IsCancellationRequested)
                break;

            try
            {
                using var scope = scopeFactory.CreateScope();
                var job = scope.ServiceProvider.GetRequiredService<StreakMaintenanceJob>();
                await job.RunAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "StreakMaintenanceWorker: job failed, will retry next night");
            }
        }
    }

    // 00:15 UTC — offset from TierDowngrade (00:00) so the two nightly jobs
    // don't compete for the same tables/connections at the same instant
    internal static DateTime NextRunAt(DateTime now)
    {
        var todayRun = now.Date.AddMinutes(15);
        return now <= todayRun ? todayRun : todayRun.AddDays(1);
    }
}
