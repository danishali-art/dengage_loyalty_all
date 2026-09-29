using dEngage.Loyalty.RuleEngine.Processing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace dEngage.Loyalty.Consumer;

// CR-08 (docs/scope-change-rules A8) — see DelayedPostingPromotionJob for why this runs hourly
// rather than the once-nightly cadence PointsExpirationWorker uses.
public class DelayedPostingPromotionWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<DelayedPostingPromotionWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        logger.LogInformation("DelayedPostingPromotionWorker: started, interval={Interval}", Interval);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var job = scope.ServiceProvider.GetRequiredService<DelayedPostingPromotionJob>();
                await job.RunAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "DelayedPostingPromotionWorker: run failed, will retry next interval");
            }

            try
            {
                await Task.Delay(Interval, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
