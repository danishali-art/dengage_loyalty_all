namespace dEngage.Loyalty.Api.Framework.Bootstrap;

// The bridge middleware creates one DI scope per HTTP request and stores it here (AsyncLocal) before
// handing off to the Nancy engine, so the bootstrapper's GetModule/GetAllModules overrides — which
// only ever receive Nancy's own TinyIoC container — can resolve modules (and everything they depend
// on, including scoped services like the DbContext) from the correct per-request scope instead.
public static class AmbientServiceProviderAccessor
{
    private static readonly AsyncLocal<IServiceProvider?> Holder = new();

    public static IServiceProvider? Current
    {
        get => Holder.Value;
        set => Holder.Value = value;
    }
}
