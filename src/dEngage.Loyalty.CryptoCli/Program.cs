using dEngage.Loyalty.Shared.Security;

try
{
    switch (args.FirstOrDefault())
    {
        case "genkey":
            Console.WriteLine(SecretCrypto.GenerateKey());
            break;

        case "encrypt" when args.Length == 2:
            Console.WriteLine(SecretCrypto.Encrypt(args[1], SecretCrypto.LoadKeyFromEnv()));
            break;

        case "decrypt" when args.Length == 2:
            Console.WriteLine(SecretCrypto.Decrypt(args[1], SecretCrypto.LoadKeyFromEnv()));
            break;

        default:
            Console.WriteLine(
                $"""
                dEngage.Loyalty.CryptoCli — ENC(...) generator for appsettings secrets

                Usage:
                  dotnet run -- genkey                     generates a new 32-byte master key (base64)
                  dotnet run -- encrypt "<plain text>"      encrypts with {SecretCrypto.KeyEnvVar}, prints ENC(...)
                  dotnet run -- decrypt "ENC(...)"         decrypts an ENC(...) value (for verification)

                encrypt/decrypt require the {SecretCrypto.KeyEnvVar} environment variable to be set:
                  export {SecretCrypto.KeyEnvVar}="$(dotnet run -- genkey)"
                """);
            Environment.ExitCode = 1;
            break;
    }
}
catch (InvalidOperationException ex)
{
    Console.Error.WriteLine($"ERROR: {ex.Message}");
    Environment.ExitCode = 2;
}
