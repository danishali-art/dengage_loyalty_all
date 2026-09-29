using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using dEngage.Loyalty.Api.Framework.Options;
using dEngage.Loyalty.Schema.Entities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace dEngage.Loyalty.Api.Framework.Auth;

public interface IJwtTokenService
{
    string IssueToken(AdminUser user);
    AuthenticatedPrincipal? ValidateToken(string token);
}

public sealed class JwtTokenService(IOptions<JwtOptions> options) : IJwtTokenService
{
    private readonly JwtOptions _options = options.Value;

    public string IssueToken(AdminUser user)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            // Plain "role" claim name, not ClaimTypes.Role — the frontend decodes the JWT payload
            // directly (jwt-decode) and expects a short key, not the long .NET claims URI.
            new("role", user.Role)
        };
        // The claim carries the tenant's external slug, not its internal Guid — callers must
        // have the Tenant navigation loaded (see AuthAppService.LoginAsync's .Include(u => u.Tenant)).
        if (user.Tenant is not null)
            claims.Add(new Claim("tenant_id", user.Tenant.Slug));

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(_options.ExpiryMinutes),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public AuthenticatedPrincipal? ValidateToken(string token)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
        // Without this, JwtSecurityTokenHandler silently remaps short inbound claim names (e.g.
        // "sub") to long ClaimTypes.* URIs, so a literal JwtRegisteredClaimNames.Sub lookup below
        // would never find anything post-validation even though the token is perfectly valid.
        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };

        ClaimsPrincipal principal;
        try
        {
            principal = handler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = _options.Issuer,
                ValidateAudience = true,
                ValidAudience = _options.Audience,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = key,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromSeconds(30)
            }, out _);
        }
        catch (SecurityTokenException)
        {
            return null;
        }

        var sub = principal.FindFirstValue(JwtRegisteredClaimNames.Sub);
        var role = principal.FindFirstValue("role");
        if (sub is null || role is null)
            return null;

        return new AuthenticatedPrincipal
        {
            Kind = PrincipalKind.AdminJwt,
            Role = role,
            TenantId = principal.FindFirstValue("tenant_id"),
            SubjectId = sub
        };
    }
}
