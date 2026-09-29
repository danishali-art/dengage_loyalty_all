using dEngage.Loyalty.RuleEngine.Processing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace dEngage.Loyalty.Consumer;

// CR-10/CR-09 — see BirthdayBonusJob. Same once-nightly cadence as PointsExpirationWorker (a
// birthday is date-granular, not fractional-day like a Delayed-posting hold).
public class BirthdayBonusWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<BirthdayBonusWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        logger.LogInformation("BirthdayBonusWorker: started");

        while (!ct.IsCancellationRequested)
        {
            var nextRun = NextRunAt(DateTime.UtcNow);
            var delay = nextRun - DateTime.UtcNow;

            logger.LogInformation("BirthdayBonusWorker: next run at {NextRun:O} (in {Minutes:F0} min)", nextRun, delay.TotalMinutes);
            await Task.Delay(delay, ct);
            if (ct.IsCancellationRequested) break;

            try
            {
                using var scope = scopeFactory.CreateScope();
                var job = scope.ServiceProvider.GetRequiredService<BirthdayBonusJob>();
                await job.RunAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "BirthdayBonusWorker: run failed, will retry tomorrow");
            }
        }
    }

    // Every night at 02:00 UTC — before the tier batch (00:00)/expiry (03:00), so a birthday
    // bonus lands before that same night's other jobs run.
    internal static DateTime NextRunAt(DateTime now)
    {
        var todayRun = now.Date.AddHours(2);
        return now <= todayRun ? todayRun : todayRun.AddDays(1);
    }
}
