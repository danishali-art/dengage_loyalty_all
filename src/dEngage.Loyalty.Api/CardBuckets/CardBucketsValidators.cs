using FluentValidation;
using dEngage.Loyalty.Shared;

namespace dEngage.Loyalty.Api.CardBuckets;

public sealed class CreateCardBucketRequestValidator : AbstractValidator<CreateCardBucketRequest>
{
    public CreateCardBucketRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(255);
        RuleFor(x => x.RewardAmount).GreaterThan(0);
        RuleFor(x => x.AmountMax).GreaterThanOrEqualTo(x => x.AmountMin!.Value)
            .When(x => x.AmountMin is not null && x.AmountMax is not null)
            .WithMessage("Amount max must be greater than or equal to amount min.");
        RuleFor(x => x.HourFrom).InclusiveBetween(0, 23).When(x => x.HourFrom is not null);
        RuleFor(x => x.HourTo).InclusiveBetween(0, 23).When(x => x.HourTo is not null);
        RuleFor(x => x.CountryMode).Must(m => m is "in" or "not_in")
            .When(x => x.Countries is { Count: > 0 })
            .WithMessage("countryMode must be 'in' or 'not_in' when countries are specified.");
        RuleFor(x => x.Countries).NotEmpty()
            .When(x => x.CountryMode is not null)
            .WithMessage("countries must be non-empty when countryMode is set.");
    }
}

public sealed class UpdateCardBucketRequestValidator : AbstractValidator<UpdateCardBucketRequest>
{
    public UpdateCardBucketRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(255).When(x => x.Name is not null);
        RuleFor(x => x.RewardAmount).GreaterThan(0).When(x => x.RewardAmount is not null);
        RuleFor(x => x.AmountMax).GreaterThanOrEqualTo(x => x.AmountMin!.Value)
            .When(x => x.AmountMin is not null && x.AmountMax is not null)
            .WithMessage("Amount max must be greater than or equal to amount min.");
        RuleFor(x => x.HourFrom).InclusiveBetween(0, 23).When(x => x.HourFrom is not null);
        RuleFor(x => x.HourTo).InclusiveBetween(0, 23).When(x => x.HourTo is not null);
    }
}

public sealed class SetCardBucketStatusRequestValidator : AbstractValidator<SetCardBucketStatusRequest>
{
    public SetCardBucketStatusRequestValidator()
    {
        RuleFor(x => x.Status).Must(s => s is RuleStatus.Active or RuleStatus.Disabled)
            .WithMessage("Status must be 'active' or 'disabled'.");
    }
}
