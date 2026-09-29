using dEngage.Loyalty.RuleEngine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace dEngage.Loyalty.Consumer;

public class TierDowngradeWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<TierDowngradeWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        logger.LogInformation("TierDowngradeWorker: started");

        while (!ct.IsCancellationRequested)
        {
            var nextRun = NextRunAt(DateTime.UtcNow);
            var delay = nextRun - DateTime.UtcNow;

            logger.LogInformation("TierDowngradeWorker: next run at {NextRun:O} (in {Minutes:F0} min)",
                nextRun, delay.TotalMinutes);

            await Task.Delay(delay, ct);

            if (ct.IsCancellationRequested)
                break;

            try
            {
                using var scope = scopeFactory.CreateScope();
                var job = scope.ServiceProvider.GetRequiredService<TierDowngradeJob>();
                await job.RunAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "TierDowngradeWorker: job failed, will retry next night");
            }
        }
    }

    // Every night at 00:00 UTC — before the expire batch (03:00)
    internal static DateTime NextRunAt(DateTime now)
    {
        var todayRun = now.Date; // 00:00
        return now <= todayRun ? todayRun : todayRun.AddDays(1);
    }
}
