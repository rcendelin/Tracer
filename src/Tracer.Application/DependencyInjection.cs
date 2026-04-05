using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Tracer.Application.Behaviors;
using Tracer.Application.Services;

namespace Tracer.Application;

/// <summary>
/// Registers Application layer services in the DI container.
/// </summary>
public static class ApplicationServiceRegistration
{
    /// <summary>
    /// Adds Application layer services: MediatR handlers, FluentValidation validators,
    /// and the validation pipeline behavior.
    /// </summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = typeof(ApplicationServiceRegistration).Assembly;

        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(assembly));
        services.AddValidatorsFromAssembly(assembly);
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));

        services.AddScoped<IWaterfallOrchestrator, WaterfallOrchestrator>();
        services.AddSingleton<IConfidenceScorer, ConfidenceScorer>();

        return services;
    }
}
