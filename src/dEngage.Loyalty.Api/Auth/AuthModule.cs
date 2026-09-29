using FluentValidation;
using dEngage.Loyalty.Api.Framework.Json;
using dEngage.Loyalty.Api.Framework.Validation;
using Nancy;

namespace dEngage.Loyalty.Api.Auth;

// /api/v1/auth/login — unauthenticated; issues the JWT every other dashboard route requires.
public sealed class AuthModule : NancyModule
{
    public AuthModule(IAuthAppService appService, IValidator<LoginRequest> loginValidator)
    {
        Post("/api/v1/auth/login", async (_, ct) =>
        {
            var request = await Request.ReadValidatedJsonBodyAsync(loginValidator, ct);
            return JsonResponses.Ok(await appService.LoginAsync(request, ct));
        });
    }
}
