using FluentAssertions;
using dEngage.Loyalty.Shared.Security;
using Xunit;

namespace dEngage.Loyalty.Engine.Tests;

// Platform alignment D3: a secret stored in the DB must be exactly one ENC(...) token. Plaintext,
// or plaintext with a token inside it, is refused instead of being (partly) used as is.
// Changes the key environment variables, so it shares SecretCryptoKeyLoadingTests' non-parallel collection.
[Collection(nameof(SecretCryptoKeyLoadingTests))]
public sealed class SecretProtectorTests : IDisposable
{
    private readonly string? _savedKey = Environment.GetEnvironmentVariable(SecretCrypto.KeyEnvVar);
    private readonly string? _savedKeyFile = Environment.GetEnvironmentVariable(SecretCrypto.KeyFileEnvVar);
    private readonly byte[] _key;

    public SecretProtectorTests()
    {
        var key = SecretCrypto.GenerateKey();
        Environment.SetEnvironmentVariable(SecretCrypto.KeyFileEnvVar, null);
        Environment.SetEnvironmentVariable(SecretCrypto.KeyEnvVar, key);
        _key = Convert.FromBase64String(key);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(SecretCrypto.KeyEnvVar, _savedKey);
        Environment.SetEnvironmentVariable(SecretCrypto.KeyFileEnvVar, _savedKeyFile);
    }

    [Fact]
    public void A_single_token_is_decrypted()
    {
        var protector = new SecretCryptoProtector();
        protector.Unprotect(SecretCrypto.Encrypt("s3cret", _key)).Should().Be("s3cret");
    }

    [Fact]
    public void Surrounding_whitespace_is_ignored()
    {
        var protector = new SecretCryptoProtector();
        protector.Unprotect($"  {SecretCrypto.Encrypt("s3cret", _key)}\n").Should().Be("s3cret");
    }

    [Fact]
    public void Protect_and_unprotect_round_trip()
    {
        var protector = new SecretCryptoProtector();
        protector.Unprotect(protector.Protect("s3cret")).Should().Be("s3cret");
    }

    [Theory]
    [InlineData("not-encrypted")]
    [InlineData("")]
    [InlineData("prefix {0}")]
    [InlineData("{0} suffix")]
    [InlineData("{0}{0}")]
    [InlineData("Host=x;Password={0}")]
    public void Anything_but_a_single_token_is_refused(string format)
    {
        var stored = string.Format(format, SecretCrypto.Encrypt("s3cret", _key));
        var act = () => new SecretCryptoProtector().Unprotect(stored);
        act.Should().Throw<InvalidOperationException>().WithMessage("secret_not_encrypted*");
    }
}
