namespace dEngage.Loyalty.Api.Framework.Tenancy;

// AsyncLocal, not tied to HttpContext, so services below the controllers can read the request's
// tenant context without depending on ASP.NET Core. Set by LoyaltyApiMiddleware.
public interface ITenantContextAccessor
{
    TenantContext? Current { get; set; }
}

public sealed class TenantContextAccessor : ITenantContextAccessor
{
    private static readonly AsyncLocal<TenantContext?> Holder = new();

    public TenantContext? Current
    {
        get => Holder.Value;
        set => Holder.Value = value;
    }
}
