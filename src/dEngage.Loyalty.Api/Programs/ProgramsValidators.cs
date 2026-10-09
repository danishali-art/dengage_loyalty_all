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

// CR 2026-09-30 (A5): lowercase letters, digits and single hyphens. No '_' — it separates the
// slug from the rest of a reward name ({slug}_{suffix}), so allowing it would let two programs
// claim the same reward name.
internal static class ProgramSlugRules
{
    public const string Pattern = "^[a-z0-9]+(-[a-z0-9]+)*$";
    public const string Message =
        "slug must be 2-40 lowercase letters, digits and single hyphens (no underscores), e.g. 'fintech' or 'fin-tech'.";
}

public sealed class CreateProgramRequestValidator : AbstractValidator<CreateProgramRequest>
{
    public CreateProgramRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(255);
        RuleFor(x => x.Slug).Length(2, 40).Matches(ProgramSlugRules.Pattern).WithMessage(ProgramSlugRules.Message)
            .When(x => x.Slug is not null);
        RuleFor(x => x.Status).Null().WithMessage(MovedFieldMessages.CreateStatus);
        RuleFor(x => x.QualifyingAccountTypeId).Null().WithMessage(MovedFieldMessages.QualifyingAccountType);
        RuleFor(x => x.WarningDays).Null().WithMessage(MovedFieldMessages.WarningDays);
        RuleFor(x => x.DefaultRounding).Must(r => RoundingDirection.All.Contains(r)).When(x => x.DefaultRounding is not null)
            .WithMessage(DefaultRoundingMessage.Text);
    }
}

// CR 2026-10-06 Phase 4.
internal static class DefaultRoundingMessage
{
    public const string Text = "defaultRounding must be 'down', 'nearest' or 'up'.";
}

public sealed class UpdateProgramRequestValidator : AbstractValidator<UpdateProgramRequest>
{
    public UpdateProgramRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(255).When(x => x.Name is not null);
        RuleFor(x => x.Slug).Length(2, 40).Matches(ProgramSlugRules.Pattern).WithMessage(ProgramSlugRules.Message)
            .When(x => x.Slug is not null);
        RuleFor(x => x.Status).Must(s => s is ProgramStatus.Active or ProgramStatus.Inactive)
            .When(x => x.Status is not null)
            .WithMessage("Status must be 'active' or 'inactive'.");
        RuleFor(x => x.QualifyingAccountTypeId).Null().WithMessage(MovedFieldMessages.QualifyingAccountType);
        RuleFor(x => x.WarningDays).Null().WithMessage(MovedFieldMessages.WarningDays);
        RuleFor(x => x.DefaultRounding).Must(r => RoundingDirection.All.Contains(r)).When(x => x.DefaultRounding is not null)
            .WithMessage(DefaultRoundingMessage.Text);
    }
}
