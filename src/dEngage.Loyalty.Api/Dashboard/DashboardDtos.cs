using dEngage.Loyalty.Api.Complaints;

namespace dEngage.Loyalty.Api.Dashboard;

public sealed record DashboardSummaryResponse(
    int ProgramCount,
    int TierCount,
    int CustomerAccountCount,
    decimal TotalBalance,
    int RedemptionCount,
    int StreakActiveCount,
    int StreakCompletedInRange,
    ComplaintSummaryResponse Complaints);
