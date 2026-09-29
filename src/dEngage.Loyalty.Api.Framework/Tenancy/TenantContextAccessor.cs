namespace dEngage.Loyalty.Api.Framework.Tenancy;

// AsyncLocal, not tied to HttpContext: Nancy modules never see HttpContext (the bridge translates
// requests before handing off), so ambient request state flows through the async call chain instead.
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
