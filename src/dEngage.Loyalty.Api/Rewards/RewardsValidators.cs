using System.Text.Json;
using FluentValidation;
using dEngage.Loyalty.Shared;

namespace dEngage.Loyalty.Api.Rewards;

public sealed class CreateRewardRequestValidator : AbstractValidator<CreateRewardRequest>
{
    public CreateRewardRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(255);

        RuleFor(x => x.Acquisition).Must(a => a is RewardAcquisition.PointsPurchase
                or RewardAcquisition.StampCompletion or RewardAcquisition.StreakCompletion)
            .WithMessage("Acquisition must be 'points_purchase', 'stamp_completion', or 'streak_completion'.");

        RuleFor(x => x.RewardType).Must(RewardTypeRegistry.IsKnown)
            .WithMessage(x => $"RewardType must be one of: {string.Join(", ", RewardTypeRegistry.KnownTypes)}.");

        // Acquisition-specific fields, both ways: required for the acquisition type they belong
        // to, and rejected for every other one — a stray StampAccountTypeId on a points_purchase
        // reward is as invalid as a missing one on a stamp_completion reward.
        RuleFor(x => x.PointsPrice).NotNull().GreaterThan(0)
            .When(x => x.Acquisition == RewardAcquisition.PointsPurchase)
            .WithMessage("PointsPrice is required for points_purchase rewards.");
        RuleFor(x => x.PointsAccountTypeId).NotNull()
            .When(x => x.Acquisition == RewardAcquisition.PointsPurchase)
            .WithMessage("PointsAccountTypeId is required for points_purchase rewards.");
        RuleFor(x => x.StampAccountTypeId).NotNull()
            .When(x => x.Acquisition == RewardAcquisition.StampCompletion)
            .WithMessage("StampAccountTypeId is required for stamp_completion rewards.");

        RuleFor(x => x.StampAccountTypeId).Null()
            .When(x => x.Acquisition != RewardAcquisition.StampCompletion)
            .WithMessage("StampAccountTypeId must only be set for stamp_completion rewards.");
        RuleFor(x => x.PointsPrice).Null()
            .When(x => x.Acquisition != RewardAcquisition.PointsPurchase)
            .WithMessage("PointsPrice must only be set for points_purchase rewards.");
        RuleFor(x => x.PointsAccountTypeId).Null()
            .When(x => x.Acquisition != RewardAcquisition.PointsPurchase)
            .WithMessage("PointsAccountTypeId must only be set for points_purchase rewards.");

        // TypeConfig shape depends on RewardType — dispatched through the registry rather than
        // branched here, so a new RewardType never needs a change to this validator.
        RuleFor(x => x).Custom((request, context) =>
        {
            if (!RewardTypeRegistry.IsKnown(request.RewardType)) return; // already reported above
            var cfg = request.TypeConfig ?? default;
            foreach (var error in RewardTypeRegistry.ValidateTypeConfig(request.RewardType, cfg))
                context.AddFailure(nameof(request.TypeConfig), error);
        });
    }
}

public sealed class UpdateRewardRequestValidator : AbstractValidator<UpdateRewardRequest>
{
    public UpdateRewardRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100).When(x => x.Name is not null);
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(255).When(x => x.DisplayName is not null);
        RuleFor(x => x.PointsPrice).GreaterThan(0).When(x => x.PointsPrice is not null);

        // TypeConfig's required shape depends on the reward's (immutable) RewardType, which this
        // DTO doesn't carry — RewardsAppService.UpdateAsync re-validates it against the existing
        // entity's RewardType once loaded.
    }
}
