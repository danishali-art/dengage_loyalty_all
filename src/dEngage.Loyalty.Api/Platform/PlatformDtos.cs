namespace dEngage.Loyalty.Api.Platform;

public sealed record CreateTenantRequest(string Id, string Name);
public sealed record UpdateTenantRequest(string? Name, string? Status);
public sealed record TenantResponse(string Id, string Name, string Status, DateTime CreatedAt);

public sealed record CreateApiKeyResponse(Guid Id, string Prefix, string RawKey, DateTime CreatedAt);
public sealed record ApiKeyResponse(Guid Id, string Prefix, DateTime CreatedAt, DateTime? LastUsedAt, DateTime? RevokedAt);

public sealed record CreateAdminUserRequest(string Email, string Password, string Role);
public sealed record AdminUserResponse(Guid Id, string Email, string Role, string? TenantId, string Status, DateTime CreatedAt);
