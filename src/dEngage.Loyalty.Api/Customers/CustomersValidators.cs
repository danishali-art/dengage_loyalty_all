using System.Globalization;
using FluentValidation;

namespace dEngage.Loyalty.Api.Customers;

// CR-10 (A11): MonthDay only, never a full date — rejects both malformed strings and
// calendar-invalid combinations (e.g. "02-30"), but accepts "02-29" (a customer born on a leap
// day) — see RuleEngine.Processing.BirthdayBonusJob for the leap-day policy that observes it.
public sealed class RegisterBirthdayRequestValidator : AbstractValidator<RegisterBirthdayRequest>
{
    public RegisterBirthdayRequestValidator()
    {
        RuleFor(x => x.MonthDay)
            .NotEmpty()
            .Must(BeAValidMonthDay)
            .WithMessage("MonthDay must be a valid 'MM-DD' calendar date (e.g. '02-29' for a leap day, but not '02-30').");
    }

    private static bool BeAValidMonthDay(string value)
    {
        // A leap year (2024) so "02-29" validates — the month/day pair itself is what's stored,
        // not any particular year.
        return DateTime.TryParseExact($"2024-{value}", "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out _);
    }
}
