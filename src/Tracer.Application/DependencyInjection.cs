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
        services.AddSingleton<IGoldenRecordMerger, GoldenRecordMerger>();
        services.AddSingleton<ICompanyNameNormalizer, CompanyNameNormalizer>();
        services.AddSingleton<IFuzzyNameMatcher, FuzzyNameMatcher>();

        // LLM disambiguation (B-64) — Null client is the default so the app boots without Azure OpenAI.
        // Infrastructure overrides this registration when Providers:AzureOpenAI:Endpoint is configured.
        services.AddSingleton<ILlmDisambiguatorClient, NullLlmDisambiguatorClient>();
        services.AddScoped<ILlmDisambiguator, LlmDisambiguator>();

        services.AddScoped<IEntityResolver, EntityResolver>();
        services.AddSingleton<IChangeDetector, ChangeDetector>();
        services.AddScoped<ICkbPersistenceService, CkbPersistenceService>();

        return services;
    }
}
