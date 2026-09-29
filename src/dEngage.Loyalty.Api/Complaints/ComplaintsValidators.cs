using FluentValidation;
using dEngage.Loyalty.Shared;

namespace dEngage.Loyalty.Api.Complaints;

public sealed class CreateComplaintRequestValidator : AbstractValidator<CreateComplaintRequest>
{
    public CreateComplaintRequestValidator()
    {
        RuleFor(x => x.Subject).NotEmpty().MaximumLength(255);
        RuleFor(x => x.CustomerKey).MaximumLength(255);
    }
}

public sealed class UpdateComplaintStatusRequestValidator : AbstractValidator<UpdateComplaintStatusRequest>
{
    public UpdateComplaintStatusRequestValidator()
    {
        RuleFor(x => x.Status).Must(s => s is ComplaintStatus.Open or ComplaintStatus.InProgress or ComplaintStatus.Resolved)
            .WithMessage("Status must be 'open', 'in_progress', or 'resolved'.");
    }
}
