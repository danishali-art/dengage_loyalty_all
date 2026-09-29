namespace dEngage.Loyalty.Api.Framework.Bootstrap;

// Populated at startup by AddNancyModule<T>(). A single production process only ever boots once,
// so registration there is effectively single-threaded — but this is static, process-wide state,
// and an in-process test host (WebApplicationFactory) re-runs Program.Main on every factory it
// creates. xUnit parallelizes across test classes by default, so two factories booting at once
// raced on this collection and corrupted it — hence the lock, cheap here since Register() only
// ever runs a handful of times at startup.
//
// Nancy's DefaultNancyBootstrapper turns out to seal every extension point for substituting how a
// module INSTANCE is produced (GetModule, GetAllModules, and RegisterRequestContainerModules are
// all sealed by the time you reach it — verified empirically, not just from docs). The one thing
// that isn't sealed is ConfigureRequestContainer, and TinyIoC's reflection-based constructor
// injection DOES consult the registration table for each individual constructor PARAMETER type
// (as opposed to the top-level module type, which DefaultNancyBootstrapper always constructs via
// raw reflection regardless of any registration for that exact type). So instead of fighting
// module construction, this lets TinyIoC construct the module via reflection as normal, and
// pre-registers a factory for every type that reflection will ask for — each module's constructor
// parameter types — so those factories resolve from ASP.NET Core DI instead of TinyIoC's own
// (unaware of DbContext/Redis/etc.) auto-resolution.
public static class NancyModuleTypeRegistry
{
    private static readonly object Lock = new();
    private static readonly List<Type> ModuleTypes = [];
    private static readonly HashSet<Type> InjectableTypes = [];

    public static IReadOnlyList<Type> All
    {
        get { lock (Lock) return ModuleTypes.ToList(); }
    }

    public static IReadOnlyCollection<Type> InjectableDependencyTypes
    {
        get { lock (Lock) return InjectableTypes.ToList(); }
    }

    public static void Register(Type moduleType)
    {
        lock (Lock)
        {
            ModuleTypes.Add(moduleType);

            var constructor = moduleType.GetConstructors()
                .OrderByDescending(c => c.GetParameters().Length)
                .FirstOrDefault();

            if (constructor is null)
                return;

            foreach (var parameter in constructor.GetParameters())
                InjectableTypes.Add(parameter.ParameterType);
        }
    }
}
