using dEngage.Loyalty.Api.Framework.ErrorHandling;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Nancy;
using Nancy.Bootstrapper;
using Nancy.TinyIoc;
using Npgsql;

namespace dEngage.Loyalty.Api.Framework.Bootstrap;

// Bridges Nancy's module resolution onto ASP.NET Core DI instead of Nancy's own TinyIoC container
// (see plan §1 — the composition root is shared with the outer host). TinyIoC is still present
// internally (Nancy requires it), it just never resolves our own modules or services.
public sealed class CompositionRootNancyBootstrapper(IServiceProvider rootProvider, IHostEnvironment environment)
    : DefaultNancyBootstrapper
{
    // Nancy's default reflection/assembly-scanning module discovery finds nothing on this runtime
    // (verified empirically — DefaultNancyBootstrapper.Modules comes back empty even for a
    // trivial parameterless module), so scanning is bypassed entirely in favor of the explicit
    // registry AddNancyModule<T>() builds at startup.
    protected override IEnumerable<ModuleRegistration> Modules =>
        NancyModuleTypeRegistry.All.Select(t => new ModuleRegistration(t));

    // See NancyModuleTypeRegistry for why this targets constructor PARAMETER types rather than
    // trying (and failing — every such attempt is sealed) to intercept module construction itself.
    //
    // Nancy's route cache — and every module in it — is built exactly once, at engine-construction
    // time (the engine is a singleton internally), which happens with no request in flight. At that
    // moment AmbientServiceProviderAccessor.Current is null, so this used to fall back to the root
    // provider directly — which correctly throws for a Scoped dependency like LoyaltyDbContext
    // (verified empirically). A short-lived scope, created only for that one-time build, resolves
    // it validly without weakening the real per-request scoping used afterward for every actual
    // request (ConfigureRequestContainer runs again per request, always finding a real ambient
    // scope from NancyAspNetCoreMiddleware then).
    protected override void ConfigureRequestContainer(TinyIoCContainer container, NancyContext context)
    {
        base.ConfigureRequestContainer(container, context);

        var provider = AmbientServiceProviderAccessor.Current ?? rootProvider.CreateScope().ServiceProvider;
        foreach (var dependencyType in NancyModuleTypeRegistry.InjectableDependencyTypes)
        {
            container.Register(dependencyType, (_, _) => provider.GetRequiredService(dependencyType));
        }
    }

    protected override void ApplicationStartup(TinyIoCContainer container, IPipelines pipelines)
    {
        base.ApplicationStartup(container, pipelines);

        pipelines.BeforeRequest += AuthenticationPipelineHook.HandleAsync;

        pipelines.OnError += (context, exception) =>
        {
            var traceId = context.Items.TryGetValue("CorrelationId", out var id)
                ? (string)id
                : Guid.NewGuid().ToString("N");

            var includeDetail = environment.IsDevelopment();

            return exception switch
            {
                AggregateValidationApiException vex => (object)ErrorResponseFactory.FromValidationFailure(traceId, vex.Errors),
                ApiException apiEx => ErrorResponseFactory.FromApiException(apiEx, traceId, includeDetail),
                InvalidOperationException ioe => ErrorResponseFactory.FromApiException(
                    DomainErrorTranslator.Translate(ioe), traceId, includeDetail),
                DbUpdateException { InnerException: PostgresException { SqlState: "23505" } } =>
                    ErrorResponseFactory.FromApiException(
                        new ConflictApiException("unique_violation", "A record with the same unique value already exists."),
                        traceId, includeDetail),
                _ => ErrorResponseFactory.FromUnexpectedException(traceId, includeDetail, exception)
            };
        };
    }
}
