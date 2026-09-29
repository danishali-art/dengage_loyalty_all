using FluentValidation;

namespace dEngage.Loyalty.Api.Tiers;

public sealed class CreateTierRequestValidator : AbstractValidator<CreateTierRequest>
{
    public CreateTierRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(255);
        RuleFor(x => x.MinPoints).GreaterThanOrEqualTo(0);
        RuleFor(x => x.QualifyingModel).Must(m => m is "periodic" or "lifetime")
            .WithMessage("QualifyingModel must be 'periodic' or 'lifetime'.");
        RuleFor(x => x.QualifyingPeriodDays).GreaterThan(0)
            .When(x => x.QualifyingModel == "periodic")
            .WithMessage("QualifyingPeriodDays is required and must be positive when QualifyingModel is 'periodic'.");
        RuleFor(x => x.GraceDays).GreaterThanOrEqualTo(0);
        RuleFor(x => x.SortOrder).GreaterThanOrEqualTo(0);
    }
}

public sealed class UpdateTierRequestValidator : AbstractValidator<UpdateTierRequest>
{
    public UpdateTierRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100).When(x => x.Name is not null);
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(255).When(x => x.DisplayName is not null);
        RuleFor(x => x.MinPoints).GreaterThanOrEqualTo(0).When(x => x.MinPoints is not null);
        RuleFor(x => x.QualifyingModel).Must(m => m is "periodic" or "lifetime")
            .When(x => x.QualifyingModel is not null)
            .WithMessage("QualifyingModel must be 'periodic' or 'lifetime'.");
        RuleFor(x => x.GraceDays).GreaterThanOrEqualTo(0).When(x => x.GraceDays is not null);
    }
}

public sealed class ReorderTiersRequestValidator : AbstractValidator<ReorderTiersRequest>
{
    public ReorderTiersRequestValidator()
    {
        RuleFor(x => x.TierIds).NotEmpty();
    }
}
