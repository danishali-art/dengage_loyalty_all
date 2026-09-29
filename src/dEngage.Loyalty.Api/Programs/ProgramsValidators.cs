using FluentValidation;
using dEngage.Loyalty.Shared;

namespace dEngage.Loyalty.Api.Programs;

// 1.3.CL item 1: QualifyingAccountTypeId and WarningDays moved to the account type. They stay on
// the request records so an old client that still sends them gets a clear 400 instead of the
// value being silently dropped.
internal static class MovedFieldMessages
{
    public const string QualifyingAccountType =
        "field_moved_to_account_type: 'qualifyingAccountTypeId' is set on the account type now (isTierQualifying).";
    public const string WarningDays =
        "field_moved_to_account_type: 'warningDays' is set on the POINTS account type config now (warning_days).";
    // 1.3.CL item 8: every program starts Draft + inactive; it can only be activated once published.
    public const string CreateStatus =
        "status_not_settable_on_create: a new program always starts as a draft and inactive — publish it, then activate it.";
}

public sealed class CreateProgramRequestValidator : AbstractValidator<CreateProgramRequest>
{
    public CreateProgramRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(255);
        RuleFor(x => x.Status).Null().WithMessage(MovedFieldMessages.CreateStatus);
        RuleFor(x => x.QualifyingAccountTypeId).Null().WithMessage(MovedFieldMessages.QualifyingAccountType);
        RuleFor(x => x.WarningDays).Null().WithMessage(MovedFieldMessages.WarningDays);
    }
}

public sealed class UpdateProgramRequestValidator : AbstractValidator<UpdateProgramRequest>
{
    public UpdateProgramRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(255).When(x => x.Name is not null);
        RuleFor(x => x.Status).Must(s => s is ProgramStatus.Active or ProgramStatus.Inactive)
            .When(x => x.Status is not null)
            .WithMessage("Status must be 'active' or 'inactive'.");
        RuleFor(x => x.QualifyingAccountTypeId).Null().WithMessage(MovedFieldMessages.QualifyingAccountType);
        RuleFor(x => x.WarningDays).Null().WithMessage(MovedFieldMessages.WarningDays);
    }
}
