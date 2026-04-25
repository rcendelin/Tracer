using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Tracer.Application.Behaviors;
using Tracer.Application.Services;
using Tracer.Application.Services.Export;

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
        // Runner: CompositeRevalidationRunner (B-66) dispatches to lightweight (timestamp-only refresh)
        // or deep (full waterfall) based on the number of expired fields. Both concrete runners are
        // registered as themselves so the composite can inject them directly; the scheduler only sees
        // the IRevalidationRunner abstraction.
        services.AddSingleton<IRevalidationQueue, RevalidationQueue>();
        services.AddScoped<LightweightRevalidationRunner>();
        services.AddScoped<DeepRevalidationRunner>();
        services.AddScoped<IRevalidationRunner, CompositeRevalidationRunner>();

        // Field TTL policy (B-68) — merges Revalidation:FieldTtl overrides with
        // platform defaults from FieldTtl.For(). Stateless, thread-safe.
        services.AddSingleton<IFieldTtlPolicy, FieldTtlPolicy>();

        // Batch export (B-81) — scoped because implementations depend on scoped
        // repositories (which in turn hold the scoped DbContext).
        services.AddScoped<ICompanyProfileExporter, CompanyProfileExporter>();
        services.AddScoped<IChangeEventExporter, ChangeEventExporter>();

        return services;
    }
}
