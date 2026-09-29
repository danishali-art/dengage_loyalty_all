using System.Text.Json;

namespace dEngage.Loyalty.Api.AccountTypes;

// IsTierQualifying (1.3.CL item 1) is optional on requests so existing callers keep working;
// null on create means false, null on update means unchanged.
public sealed record CreateAccountTypeRequest(string Type, string Name, JsonElement Config, bool? IsTierQualifying = null);
public sealed record UpdateAccountTypeRequest(string? Name, JsonElement? Config, bool? IsTierQualifying = null);
public sealed record AccountTypeResponse(Guid Id, string Type, string Name, JsonElement Config, DateTime CreatedAt, bool IsTierQualifying);
