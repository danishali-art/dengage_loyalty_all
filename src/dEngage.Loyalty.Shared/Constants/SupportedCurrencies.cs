namespace dEngage.Loyalty.Shared;

// 1.3.CL item 4: CASH wallets hold real credit, so their currency is picked from a fixed
// whitelist instead of free text (a typo'd code would silently mislabel every balance). The
// portal mirrors this list in web/src/entities/account-type/model/types.ts — keep both in step.
public static class SupportedCurrencies
{
    public const string Default = "SAR";

    public static readonly IReadOnlyList<string> All =
        ["SAR", "AED", "KWD", "QAR", "BHD", "OMR", "USD", "EUR", "GBP", "TRY"];

    public static bool IsSupported(string? code) => code is not null && All.Contains(code);
}
