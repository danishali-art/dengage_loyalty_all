using dEngage.Loyalty.Ledger;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace dEngage.Loyalty.Consumer;

public class PointsExpiringDetectorWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<PointsExpiringDetectorWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        logger.LogInformation("PointsExpiringDetectorWorker: started");

        while (!ct.IsCancellationRequested)
        {
            var nextRun = NextRunAt(DateTime.UtcNow);
            var delay = nextRun - DateTime.UtcNow;

            logger.LogInformation("PointsExpiringDetectorWorker: next run at {NextRun:O} (in {Minutes:F0} min)",
                nextRun, delay.TotalMinutes);

            await Task.Delay(delay, ct);

            if (ct.IsCancellationRequested)
                break;

            try
            {
                using var scope = scopeFactory.CreateScope();
                var job = scope.ServiceProvider.GetRequiredService<PointsExpiringDetectorJob>();
                await job.RunAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "PointsExpiringDetectorWorker: job failed, will retry next night");
            }
        }
    }

    // Every night at 01:00 UTC — after the tier batch (00:00), before the expire batch (03:00).
    // The warning must be produced BEFORE expiry: even for the last slice expiring that same night,
    // the dedup window (expires_on) still matches yesterday's warning, so no duplicate warning appears.
    internal static DateTime NextRunAt(DateTime now)
    {
        var todayRun = now.Date.AddHours(1);
        return now <= todayRun ? todayRun : todayRun.AddDays(1);
    }
}
