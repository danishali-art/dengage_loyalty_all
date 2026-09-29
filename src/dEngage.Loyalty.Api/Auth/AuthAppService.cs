using dEngage.Loyalty.Api.Framework.Auth;
using dEngage.Loyalty.Api.Framework.ErrorHandling;
using dEngage.Loyalty.Api.Framework.Options;
using dEngage.Loyalty.Schema;
using dEngage.Loyalty.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace dEngage.Loyalty.Api.Auth;

public interface IAuthAppService
{
    Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken ct);
}

public sealed class AuthAppService(
    LoyaltyDbContext db,
    IPasswordService passwordService,
    IJwtTokenService jwtTokenService,
    IOptions<JwtOptions> jwtOptions) : IAuthAppService
{
    public async Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken ct)
    {
        var user = await db.AdminUsers.Include(u => u.Tenant).FirstOrDefaultAsync(u => u.Email == request.Email, ct);
        if (user is null || user.Status != EntityStatus.Active || !passwordService.Verify(user.PasswordHash, request.Password))
            throw new UnauthorizedApiException("Invalid email or password.");

        var token = jwtTokenService.IssueToken(user);
        return new LoginResponse(token, "Bearer", jwtOptions.Value.ExpiryMinutes * 60);
    }
}
