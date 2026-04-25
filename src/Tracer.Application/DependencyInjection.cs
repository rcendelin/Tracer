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

        // GDPR classification and audit hook (B-69). Stateless, thread-safe.
        services.AddSingleton<IGdprPolicy, GdprPolicy>();
        services.AddSingleton<IPersonalDataAccessAudit, LoggingPersonalDataAccessAudit>();

        // Re-validation (B-65) — queue is singleton (in-memory Channel), scheduler lives in Infrastructure.
        // Runner: DeepRevalidationRunner (B-67) is the default. When the profile has fewer expired fields
        // than the configured threshold it returns Deferred, matching the current placeholder behaviour
        // until the lightweight mode (B-66) replaces that branch.
        services.AddSingleton<IRevalidationQueue, RevalidationQueue>();
        services.AddScoped<IRevalidationRunner, DeepRevalidationRunner>();

        // Field TTL policy (B-68) — merges Revalidation:FieldTtl overrides with
        // platform defaults from FieldTtl.For(). Stateless, thread-safe.
        services.AddSingleton<IFieldTtlPolicy, FieldTtlPolicy>();

        return services;
    }
}
