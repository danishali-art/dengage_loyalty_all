using FluentValidation;
using dEngage.Loyalty.Shared;

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
        RuleFor(x => x.Config).NotNull();
    }
}

public sealed class UpdateStreakCampaignRequestValidator : AbstractValidator<UpdateStreakCampaignRequest>
{
    public UpdateStreakCampaignRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(255).When(x => x.Name is not null);
        RuleFor(x => x.Trigger).NotEmpty().MaximumLength(100).When(x => x.Trigger is not null);
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
