using dEngage.Loyalty.Ledger;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace dEngage.Loyalty.Consumer;

public class EventLogRetentionWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<EventLogRetentionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        logger.LogInformation("EventLogRetentionWorker: started");

        while (!ct.IsCancellationRequested)
        {
            var nextRun = NextRunAt(DateTime.UtcNow);
            var delay = nextRun - DateTime.UtcNow;

            logger.LogInformation("EventLogRetentionWorker: next run at {NextRun:O} (in {Minutes:F0} min)",
                nextRun, delay.TotalMinutes);

            await Task.Delay(delay, ct);

            if (ct.IsCancellationRequested)
                break;

            try
            {
                using var scope = scopeFactory.CreateScope();
                var job = scope.ServiceProvider.GetRequiredService<EventLogRetentionJob>();
                await job.RunAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "EventLogRetentionWorker: job failed, will retry next night");
            }
        }
    }

    // 02:00 UTC — free slot between expiring-detector (01:00) and expiry (03:00)
    internal static DateTime NextRunAt(DateTime now)
    {
        var todayRun = now.Date.AddHours(2);
        return now <= todayRun ? todayRun : todayRun.AddDays(1);
    }
}
