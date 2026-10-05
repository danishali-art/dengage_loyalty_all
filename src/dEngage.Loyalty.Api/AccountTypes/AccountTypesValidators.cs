using FluentValidation;

namespace dEngage.Loyalty.Api.AccountTypes;

public sealed class CreateAccountTypeRequestValidator : AbstractValidator<CreateAccountTypeRequest>
{
    public CreateAccountTypeRequestValidator()
    {
        // CR 2026-10-05 (D1): STAMP is retired — existing STAMP wallets stay as customer history,
        // but no new one can be created.
        RuleFor(x => x.Type).Must(t => t is "POINTS" or "CASH")
            .WithMessage("Type must be 'POINTS' or 'CASH'.");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
    }
}

public sealed class UpdateAccountTypeRequestValidator : AbstractValidator<UpdateAccountTypeRequest>
{
    public UpdateAccountTypeRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100).When(x => x.Name is not null);
    }
}
