using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace dEngage.Loyalty.Shared.Security;

/// <summary>
/// AES-256-GCM encryption for ENC(base64) tokens in config values.
/// Payload layout: nonce(12) || tag(16) || ciphertext. Key: LOYALTY_MASTER_KEY (base64, 32 bytes).
/// </summary>
public static class SecretCrypto
{
    public const string KeyEnvVar = "LOYALTY_MASTER_KEY";
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private static readonly Regex TokenPattern = new(@"ENC\(([A-Za-z0-9+/=]+)\)", RegexOptions.Compiled);

    public static string GenerateKey() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    public static byte[] LoadKeyFromEnv()
    {
        var raw = Environment.GetEnvironmentVariable(KeyEnvVar);
        if (string.IsNullOrWhiteSpace(raw))
            throw new InvalidOperationException(
                $"{KeyEnvVar} is not set — ENC(...) values in the config cannot be decrypted. " +
                "Generate a key with CryptoCli 'genkey' and export it.");

        byte[] key;
        try
        {
            key = Convert.FromBase64String(raw.Trim());
        }
        catch (FormatException)
        {
            throw new InvalidOperationException($"{KeyEnvVar} is not valid base64.");
        }

        if (key.Length != 32)
            throw new InvalidOperationException(
                $"{KeyEnvVar} must be a 32-byte AES-256 key (decoded to {key.Length} bytes).");

        return key;
    }

    public static string Encrypt(string plaintext, byte[] key)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var plainBytes = Encoding.UTF8.GetBytes(plaintext);
        var cipher = new byte[plainBytes.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, plainBytes, cipher, tag);

        var payload = new byte[NonceSize + TagSize + cipher.Length];
        nonce.CopyTo(payload, 0);
        tag.CopyTo(payload, NonceSize);
        cipher.CopyTo(payload, NonceSize + TagSize);
        return $"ENC({Convert.ToBase64String(payload)})";
    }

    public static string Decrypt(string encToken, byte[] key)
    {
        var match = TokenPattern.Match(encToken);
        var base64 = match.Success ? match.Groups[1].Value : encToken.Trim();

        byte[] payload;
        try
        {
            payload = Convert.FromBase64String(base64);
        }
        catch (FormatException)
        {
            throw new InvalidOperationException("ENC(...) content is not valid base64.");
        }

        if (payload.Length < NonceSize + TagSize)
            throw new InvalidOperationException("ENC(...) payload is shorter than expected.");

        var nonce = payload.AsSpan(0, NonceSize);
        var tag = payload.AsSpan(NonceSize, TagSize);
        var cipher = payload.AsSpan(NonceSize + TagSize);
        var plain = new byte[cipher.Length];

        using var aes = new AesGcm(key, TagSize);
        try
        {
            aes.Decrypt(nonce, cipher, tag, plain);
        }
        catch (AuthenticationTagMismatchException)
        {
            throw new InvalidOperationException(
                $"ENC(...) value could not be decrypted — {KeyEnvVar} does not match the key that encrypted it.");
        }

        return Encoding.UTF8.GetString(plain);
    }

    public static bool ContainsToken(string? value) =>
        value is not null && TokenPattern.IsMatch(value);

    /// <summary>Resolves ALL ENC(...) tokens within a value — partial use
    /// ("Host=x;Password=ENC(...)") and full-value tokens go through the same path.</summary>
    public static string ResolveTokens(string value, byte[] key) =>
        TokenPattern.Replace(value, m => Decrypt(m.Value, key));
}
