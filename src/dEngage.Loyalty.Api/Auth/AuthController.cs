using FluentValidation;
using dEngage.Loyalty.Api.Framework.Json;
using dEngage.Loyalty.Api.Framework.Validation;
using dEngage.Loyalty.Api.Framework.Controllers;
using Microsoft.AspNetCore.Mvc;


namespace dEngage.Loyalty.Api.Auth;

// /api/v1/auth/login — unauthenticated; issues the JWT every other dashboard route requires.
public sealed class AuthController(
    IAuthAppService appService, IValidator<LoginRequest> loginValidator) : LoyaltyControllerBase
{
    [HttpPost("api/v1/auth/login")]
    public async Task<IActionResult> Login(CancellationToken ct)
    {
        var request = await Request.ReadValidatedJsonBodyAsync(loginValidator, ct);
        return JsonResponses.Ok(await appService.LoginAsync(request, ct));
    }
}
