using dEngage.Loyalty.Ledger;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace dEngage.Loyalty.Consumer;

public class PointsExpirationWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<PointsExpirationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        logger.LogInformation("PointsExpirationWorker: started");

        while (!ct.IsCancellationRequested)
        {
            var nextRun = NextRunAt(DateTime.UtcNow);
            var delay = nextRun - DateTime.UtcNow;

            logger.LogInformation("PointsExpirationWorker: next run at {NextRun:O} (in {Minutes:F0} min)",
                nextRun, delay.TotalMinutes);

            await Task.Delay(delay, ct);

            if (ct.IsCancellationRequested)
                break;

            try
            {
                using var scope = scopeFactory.CreateScope();
                var job = scope.ServiceProvider.GetRequiredService<PointsExpirationJob>();
                await job.RunAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "PointsExpirationWorker: job failed, will retry next night");
            }
        }
    }

    // Every night at 03:00 UTC — after the tier batch (00:00) has finished
    internal static DateTime NextRunAt(DateTime now)
    {
        var todayRun = now.Date.AddHours(3);
        return now <= todayRun ? todayRun : todayRun.AddDays(1);
    }
}
