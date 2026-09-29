using FluentValidation;
using dEngage.Loyalty.Api.Framework.ErrorHandling;
using dEngage.Loyalty.Api.Framework.Json;
using Nancy;

namespace dEngage.Loyalty.Api.Framework.Validation;

public static class ValidationExtensions
{
    public static async Task<T> ReadValidatedJsonBodyAsync<T>(this Request request, IValidator<T> validator, CancellationToken ct)
    {
        var value = await request.ReadJsonBodyAsync<T>(ct);
        await ValidateAsync(validator, value, ct);
        return value;
    }

    public static async Task ValidateAsync<T>(IValidator<T> validator, T value, CancellationToken ct)
    {
        var result = await validator.ValidateAsync(value, ct);
        if (result.IsValid)
            return;

        var errors = result.Errors
            .GroupBy(e => e.PropertyName)
            .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());

        throw new AggregateValidationApiException(errors);
    }
}
