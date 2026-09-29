using Microsoft.AspNetCore.Identity;

namespace dEngage.Loyalty.Api.Framework.Auth;

// PasswordHasher<T> ships with ASP.NET Core Identity (PBKDF2) — reused here for both admin-user
// login passwords and API-key hashes so we don't hand-roll crypto for either.
public interface IPasswordService
{
    string Hash(string plainText);
    bool Verify(string hash, string plainText);
}

public sealed class PasswordService : IPasswordService
{
    private readonly PasswordHasher<object> _hasher = new();
    private static readonly object DummyUser = new();

    public string Hash(string plainText) => _hasher.HashPassword(DummyUser, plainText);

    public bool Verify(string hash, string plainText) =>
        _hasher.VerifyHashedPassword(DummyUser, hash, plainText) != PasswordVerificationResult.Failed;
}
