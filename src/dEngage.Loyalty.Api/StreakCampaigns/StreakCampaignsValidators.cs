using FluentValidation;
using dEngage.Loyalty.Shared;
using dEngage.Loyalty.Shared.Events;

namespace dEngage.Loyalty.Api.StreakCampaigns;

// Structural/required-field checks only — the condition/streak DSL itself is validated by
// RuleEngine.Models.ConditionDsl / StreakConfig.Validate in StreakCampaignsAppService, reused
// rather than re-implemented (same convention as RulesValidators.cs).
public sealed class CreateStreakCampaignRequestValidator : AbstractValidator<CreateStreakCampaignRequest>
{
    public CreateStreakCampaignRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(255);
        RuleFor(x => x.Trigger).NotEmpty().MaximumLength(100);
        // CR 2026-10-05 (D15): rejected by name — an unknown trigger is otherwise accepted as a
        // tenant generic type (same as RulesValidators).
        RuleFor(x => x.Trigger).NotEqual(EventTypes.PointsExpired).WithMessage("trigger_retired: 'points.expired' is no longer a trigger. Points expiry is configured on the POINTS account type.");
        RuleFor(x => x.Config).NotNull();
    }
}

public sealed class UpdateStreakCampaignRequestValidator : AbstractValidator<UpdateStreakCampaignRequest>
{
    public UpdateStreakCampaignRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(255).When(x => x.Name is not null);
        RuleFor(x => x.Trigger).NotEmpty().MaximumLength(100).When(x => x.Trigger is not null);
        RuleFor(x => x.Trigger).NotEqual(EventTypes.PointsExpired).WithMessage("trigger_retired: 'points.expired' is no longer a trigger. Points expiry is configured on the POINTS account type.");
    }
}

public sealed class SetStreakCampaignStatusRequestValidator : AbstractValidator<SetStreakCampaignStatusRequest>
{
    public SetStreakCampaignStatusRequestValidator()
    {
        RuleFor(x => x.Status).Must(s => s is RuleStatus.Active or RuleStatus.Disabled)
            .WithMessage("Status must be 'active' or 'disabled'.");
    }
}
