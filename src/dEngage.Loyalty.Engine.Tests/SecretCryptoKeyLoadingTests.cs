using FluentAssertions;
using dEngage.Loyalty.Shared.Security;
using Xunit;

namespace dEngage.Loyalty.Engine.Tests;

// Platform alignment D3: deployed environments provide the ENC(...) master key as a mounted secret
// file (LOYALTY_MASTER_KEY_FILE); the LOYALTY_MASTER_KEY variable stays as the local fallback.
// These tests change process environment variables, so they run in one non-parallel collection.
[Collection(nameof(SecretCryptoKeyLoadingTests))]
[CollectionDefinition(nameof(SecretCryptoKeyLoadingTests), DisableParallelization = true)]
public sealed class SecretCryptoKeyLoadingTests : IDisposable
{
    private readonly string? _savedKey = Environment.GetEnvironmentVariable(SecretCrypto.KeyEnvVar);
    private readonly string? _savedKeyFile = Environment.GetEnvironmentVariable(SecretCrypto.KeyFileEnvVar);
    private readonly string _keyFilePath = Path.Combine(Path.GetTempPath(), $"loyalty-key-{Guid.NewGuid():N}.txt");

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(SecretCrypto.KeyEnvVar, _savedKey);
        Environment.SetEnvironmentVariable(SecretCrypto.KeyFileEnvVar, _savedKeyFile);
        if (File.Exists(_keyFilePath)) File.Delete(_keyFilePath);
    }

    [Fact]
    public void The_key_file_wins_over_the_variable()
    {
        var fileKey = SecretCrypto.GenerateKey();
        File.WriteAllText(_keyFilePath, fileKey + "\n"); // mounted secrets often end with a newline
        Environment.SetEnvironmentVariable(SecretCrypto.KeyFileEnvVar, _keyFilePath);
        Environment.SetEnvironmentVariable(SecretCrypto.KeyEnvVar, SecretCrypto.GenerateKey());

        SecretCrypto.LoadKeyFromEnv().Should().Equal(Convert.FromBase64String(fileKey));
    }

    [Fact]
    public void The_variable_is_used_when_no_key_file_is_set()
    {
        var key = SecretCrypto.GenerateKey();
        Environment.SetEnvironmentVariable(SecretCrypto.KeyFileEnvVar, null);
        Environment.SetEnvironmentVariable(SecretCrypto.KeyEnvVar, key);

        SecretCrypto.LoadKeyFromEnv().Should().Equal(Convert.FromBase64String(key));
    }

    [Fact]
    public void A_key_file_that_does_not_exist_fails_with_its_path()
    {
        Environment.SetEnvironmentVariable(SecretCrypto.KeyFileEnvVar, _keyFilePath);
        Environment.SetEnvironmentVariable(SecretCrypto.KeyEnvVar, SecretCrypto.GenerateKey());

        var act = () => SecretCrypto.LoadKeyFromEnv();

        // No silent fallback to the variable: a wrong mount must be noticed at startup.
        act.Should().Throw<InvalidOperationException>().WithMessage($"*{_keyFilePath}*");
    }

    [Fact]
    public void Neither_source_set_fails_naming_both()
    {
        Environment.SetEnvironmentVariable(SecretCrypto.KeyFileEnvVar, null);
        Environment.SetEnvironmentVariable(SecretCrypto.KeyEnvVar, null);

        var act = () => SecretCrypto.LoadKeyFromEnv();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"*{SecretCrypto.KeyFileEnvVar}*{SecretCrypto.KeyEnvVar}*");
    }

    [Fact]
    public void A_value_encrypted_with_the_file_key_round_trips()
    {
        File.WriteAllText(_keyFilePath, SecretCrypto.GenerateKey());
        Environment.SetEnvironmentVariable(SecretCrypto.KeyFileEnvVar, _keyFilePath);
        var key = SecretCrypto.LoadKeyFromEnv();

        var token = SecretCrypto.Encrypt("Host=db;Password=s3cret", key);

        SecretCrypto.ResolveTokens($"prefix {token}", key).Should().Be("prefix Host=db;Password=s3cret");
    }
}
