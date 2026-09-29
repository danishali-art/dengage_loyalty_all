namespace dEngage.Loyalty.Api.Auth;

public sealed record LoginRequest(string Email, string Password);
public sealed record LoginResponse(string Token, string TokenType, int ExpiresInSeconds);
