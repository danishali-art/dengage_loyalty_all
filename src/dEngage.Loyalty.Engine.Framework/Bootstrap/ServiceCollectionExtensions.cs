using dEngage.Loyalty.Engine.Framework.Abstractions;
using dEngage.Loyalty.Engine.Framework.Events;
using Microsoft.Extensions.DependencyInjection;

namespace dEngage.Loyalty.Engine.Framework.Bootstrap;

public static class ServiceCollectionExtensions
{
    // Mirrors Api.Framework's AddLoyaltyApiFramework: one composition-root entry point
    // Consumer's Program.cs calls instead of hand-registering each framework piece.
    //
    // IEventPublisherSink is NOT registered here — Ledger supplies that adapter (see
    // IEventPublisherSink's remarks), so the caller must register it separately before
    // resolving IDomainEventDispatcher.
    public static IServiceCollection AddLoyaltyEngineFramework(this IServiceCollection services)
    {
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IIdGenerator, SystemIdGenerator>();
        services.AddScoped<IDomainEventDispatcher, DomainEventDispatcher>();

        return services;
    }
}
