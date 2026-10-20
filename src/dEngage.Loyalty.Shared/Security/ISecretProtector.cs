namespace dEngage.Loyalty.Shared.Security;

// Encrypts and decrypts secrets that loyalty stores in its own database (platform alignment D3):
// loyalty_config rows marked is_secret today, dEngage integration credentials later. Uses the same
// AES-256-GCM ENC(...) format and key as config values, so CryptoCli can produce stored values.
// Deliberately not the dEngage platform's SecManager: loyalty must run without dEngage (D13).
public interface ISecretProtector
{
    string Protect(string plaintext);

    // `stored` must be an ENC(...) token. A plaintext value is refused rather than passed through,
    // so a secret saved unencrypted by mistake is noticed instead of silently used.
    string Unprotect(string stored);
}

public sealed class SecretCryptoProtector : ISecretProtector
{
    // The key is only needed once a secret is actually read; deployments without secret rows
    // don't have to provide one.
    private readonly Lazy<byte[]> _key = new(SecretCrypto.LoadKeyFromEnv);

    public string Protect(string plaintext) => SecretCrypto.Encrypt(plaintext, _key.Value);

    public string Unprotect(string stored)
    {
        if (!SecretCrypto.ContainsToken(stored))
            throw new InvalidOperationException("secret_not_encrypted: a stored secret must be an ENC(...) value.");
        return SecretCrypto.ResolveTokens(stored, _key.Value);
    }
}
