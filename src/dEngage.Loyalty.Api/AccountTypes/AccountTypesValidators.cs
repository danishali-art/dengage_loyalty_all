using FluentValidation;

namespace dEngage.Loyalty.Api.AccountTypes;

public sealed class CreateAccountTypeRequestValidator : AbstractValidator<CreateAccountTypeRequest>
{
    public CreateAccountTypeRequestValidator()
    {
        RuleFor(x => x.Type).Must(t => t is "POINTS" or "CASH" or "STAMP")
            .WithMessage("Type must be 'POINTS', 'CASH', or 'STAMP'.");
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
