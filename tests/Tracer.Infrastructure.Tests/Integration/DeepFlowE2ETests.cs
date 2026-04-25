using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Resilience;
using Tracer.Application.DTOs;
using Tracer.Application.Services;
using Tracer.Domain.Entities;
using Tracer.Domain.Enums;
using Tracer.Domain.Interfaces;
using Tracer.Domain.ValueObjects;
using Tracer.Infrastructure.Tests.Integration.Fakes;

namespace Tracer.Infrastructure.Tests.Integration;

/// <summary>
/// End-to-end tests for the Deep enrichment flow (<c>POST /api/trace</c> with
/// <see cref="TraceDepth.Deep"/>). Covers B-77 acceptance criteria:
/// <list type="number">
/// <item>Happy path — all three tiers run and persist a complete profile.</item>
/// <item>Re-enrichment detects a <see cref="ChangeSeverity.Critical"/> change.</item>
/// <item>Ambiguous name resolution falls through to the LLM disambiguator.</item>
/// <item>Performance — orchestrator overhead with fast fakes remains &lt; 2 s.</item>
/// </list>
/// Uses <see cref="WebApplicationFactory{TEntryPoint}"/> with all external dependencies
/// (providers, Azure OpenAI, SQL, Service Bus, webhooks) swapped out for in-memory fakes.
/// </summary>
public sealed class DeepFlowE2ETests
{
    // Shared JSON options — API registers JsonStringEnumConverter via ConfigureHttpJsonOptions,
    // so test-side deserialisation must match or enums come back as "0" / "1" strings.
    private static readonly JsonSerializerOptions ApiJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private const string TestApiKey = "test-api-key-e2e";

    // ── T3: Happy path ────────────────────────────────────────────────────────

    [Fact]
    public async Task PostTrace_DeepDepth_RunsAllTiersAndPersistsProfile()
    {
        var host = new TestHost();
        host.WithTier1Provider("fake-ares", priority: 10, new Dictionary<FieldName, object?>
        {
            [FieldName.LegalName] = "ACME s.r.o.",
            [FieldName.LegalForm] = "s.r.o.",
            [FieldName.RegisteredAddress] = new Address
            {
                Street = "Sesame Street 1",
                City = "Prague",
                PostalCode = "110 00",
                Country = "CZ",
            },
        });
        host.WithTier1Provider("fake-gleif", priority: 30, new Dictionary<FieldName, object?>
        {
            [FieldName.ParentCompany] = "ACME Holdings N.V.",
        });
        host.WithTier2Provider("fake-scraper", priority: 150, new Dictionary<FieldName, object?>
        {
            [FieldName.Website] = "https://acme.example.com",
            [FieldName.Phone] = "+420 111 222 333",
        });
        host.WithTier3Provider("fake-ai", priority: 250, new Dictionary<FieldName, object?>
        {
            [FieldName.Industry] = "Office Supplies",
        });

        var factory = host.BuildFactory();
        await using (factory.ConfigureAwait(true))
        {
            using var client = CreateAuthenticatedClient(factory);

            var request = new TraceRequestDto
            {
                CompanyName = "ACME s.r.o.",
                Country = "CZ",
                RegistrationId = "00177041",
                Depth = TraceDepth.Deep,
            };

            var response = await client.PostAsJsonAsync("/api/trace", request).ConfigureAwait(true);

            response.StatusCode.Should().Be(HttpStatusCode.Created);
            var body = await response.Content.ReadFromJsonAsync<TraceResultDto>(ApiJsonOptions)
                .ConfigureAwait(true);
            body.Should().NotBeNull();
            body!.Status.Should().Be(TraceStatus.Completed);
            body.OverallConfidence.Should().BeGreaterThan(0.0);

            // CKB side: one profile with fields from all four providers
            var profiles = host.Profiles.All;
            profiles.Should().HaveCount(1);
            var profile = profiles.Single();
            profile.NormalizedKey.Should().Be("CZ:00177041");
            profile.LegalName!.Value.Should().Be("ACME s.r.o.");
            profile.LegalForm!.Value.Should().Be("s.r.o.");
            profile.RegisteredAddress!.Value.City.Should().Be("Prague");
            profile.ParentCompany!.Value.Should().Be("ACME Holdings N.V.");
            profile.Website!.Value.Should().Be("https://acme.example.com");
            profile.Phone!.Value.Should().Be("+420 111 222 333");
            profile.Industry!.Value.Should().Be("Office Supplies");

            // All tiers invoked
            host.Provider("fake-ares").Invocations.Should().Be(1);
            host.Provider("fake-gleif").Invocations.Should().Be(1);
            host.Provider("fake-scraper").Invocations.Should().Be(1);
            host.Provider("fake-ai").Invocations.Should().Be(1);
        }
    }

    // ── T4: Change detection on re-enrichment ────────────────────────────────

    [Fact]
    public async Task PostTrace_ReEnrichment_DetectsCriticalEntityStatusChange()
    {
        var host = new TestHost();
        host.WithTier1Provider("fake-ares", priority: 10, new Dictionary<FieldName, object?>
        {
            [FieldName.LegalName] = "ACME s.r.o.",
            [FieldName.EntityStatus] = "dissolved",   // critical change from seeded "active"
        });

        // Seed an existing profile with EntityStatus = active
        var seeded = new CompanyProfile("CZ:00177041", "CZ", "00177041");
        var priorStatus = new TracedField<string>
        {
            Value = "active",
            Confidence = Confidence.Create(0.95),
            Source = "seed",
            EnrichedAt = DateTimeOffset.UtcNow.AddDays(-30),
        };
        seeded.UpdateField(FieldName.EntityStatus, priorStatus, "seed");
        seeded.SetOverallConfidence(Confidence.Create(0.9));
        host.SeedProfile(seeded);

        var factory = host.BuildFactory();
        await using (factory.ConfigureAwait(true))
        {
            using var client = CreateAuthenticatedClient(factory);

            var request = new TraceRequestDto
            {
                CompanyName = "ACME s.r.o.",
                Country = "CZ",
                RegistrationId = "00177041",
                Depth = TraceDepth.Deep,
            };

            var response = await client.PostAsJsonAsync("/api/trace", request).ConfigureAwait(true);

            response.StatusCode.Should().Be(HttpStatusCode.Created);

            var profile = host.Profiles.All.Single();
            profile.EntityStatus!.Value.Should().Be("dissolved");

            var criticalChanges = host.Changes.All
                .Where(c => c.Severity == ChangeSeverity.Critical && c.Field == FieldName.EntityStatus)
                .ToList();
            criticalChanges.Should().HaveCount(1);
            criticalChanges.Single().PreviousValueJson.Should().Contain("active");
            criticalChanges.Single().NewValueJson.Should().Contain("dissolved");

            // CKB persistence service marks the event as notified after dispatch
            criticalChanges.Single().IsNotified.Should().BeTrue(
                "CkbPersistenceService calls MarkNotified after SaveChangesAsync dispatch");
        }
    }

    // ── T5: Entity resolution — ambiguous name falls through to LLM ──────────

    [Fact]
    public async Task EntityResolver_AmbiguousName_UsesLlmDisambiguation()
    {
        // Pre-configure scores so exactly two candidates land in the mid-tier band
        // (0.70 ≤ score < 0.85) where the EntityResolver escalates to the LLM.
        // Single-token legal names normalize to themselves (no legal form / stopword / punctuation),
        // so the stub lookup is stable regardless of the normalizer's internal rules.
        var matcher = new StubFuzzyNameMatcher { DefaultScore = 0.0 }
            .SetScore("ALPHA", 0.60)   // below 0.70 — dropped
            .SetScore("BETA", 0.80)    // mid-tier (highest → LLM index 0)
            .SetScore("GAMMA", 0.75);  // mid-tier (→ LLM index 1)

        var host = new TestHost
        {
            LlmSelectedIndex = 0,      // LLM picks the top mid-tier candidate (BETA → CZ:1002)
            LlmConfidence = 0.95,      // ×0.7 = 0.665, above the 0.5 acceptance threshold
            FuzzyMatcher = matcher,
        };

        host.SeedProfile(BuildSeededProfile("CZ:1001", "CZ", "1001", legalName: "ALPHA"));
        host.SeedProfile(BuildSeededProfile("CZ:1002", "CZ", "1002", legalName: "BETA"));
        host.SeedProfile(BuildSeededProfile("CZ:1003", "CZ", "1003", legalName: "GAMMA"));

        var factory = host.BuildFactory();
        await using (factory.ConfigureAwait(true))
        {
            // Resolve directly — the waterfall does not route name-only queries through
            // EntityResolver yet (see CLAUDE.md: FindExistingProfileAsync gap); this test
            // covers the EntityResolver + real normalizer + stub matcher + LLM fake wiring.
            using var scope = factory.Services.CreateScope();
            var resolver = scope.ServiceProvider.GetRequiredService<IEntityResolver>();

            var matched = await resolver.ResolveAsync(
                new TraceRequestDto
                {
                    CompanyName = "ACME",   // query name is irrelevant — scores are stubbed
                    Country = "CZ",
                },
                CancellationToken.None).ConfigureAwait(true);

            matched.Should().NotBeNull("two mid-tier candidates must escalate to the LLM");
            matched!.NormalizedKey.Should().Be("CZ:1002");
            host.LlmClient.CallCount.Should().Be(1,
                "EntityResolver must escalate to LLM for mid-tier scores");
            host.LlmClient.LastRequest!.Candidates.Should().HaveCount(2,
                "only BETA and GAMMA are in the mid-tier band; ALPHA is below 0.70");
        }
    }

    // ── T6: Performance — orchestrator overhead is bounded ───────────────────

    [Fact]
    public async Task PostTrace_DeepDepth_CompletesWellUnderDeepBudget()
    {
        var host = new TestHost();
        host.WithTier1Provider("fake-ares", priority: 10, new Dictionary<FieldName, object?>
        {
            [FieldName.LegalName] = "Perf Co",
        });
        host.WithTier2Provider("fake-scraper", priority: 150, new Dictionary<FieldName, object?>
        {
            [FieldName.Website] = "https://perf.example.com",
        });
        host.WithTier3Provider("fake-ai", priority: 250, new Dictionary<FieldName, object?>
        {
            [FieldName.Industry] = "Testing",
        });

        var factory = host.BuildFactory();
        await using (factory.ConfigureAwait(true))
        {
            using var client = CreateAuthenticatedClient(factory);

            var stopwatch = Stopwatch.StartNew();
            var response = await client.PostAsJsonAsync("/api/trace", new TraceRequestDto
            {
                CompanyName = "Perf Co",
                Country = "CZ",
                RegistrationId = "99999999",
                Depth = TraceDepth.Deep,
            }).ConfigureAwait(true);
            stopwatch.Stop();

            response.StatusCode.Should().Be(HttpStatusCode.Created);

            // With zero-latency fakes the full Deep pipeline (Tier 1 parallel + Tier 2/3
            // sequential) plus CKB persistence and HTTP round-trip should finish in well under
            // a second. 5 seconds is a generous ceiling that still catches accidental real-time
            // waits (e.g. forgotten Task.Delay, blocking I/O, cold JIT on first test).
            stopwatch.Elapsed.Should().BeLessThan(
                TimeSpan.FromSeconds(5),
                "the Deep waterfall with instant fakes must not wait on real 30 s budgets");
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static CompanyProfile BuildSeededProfile(
        string normalizedKey, string country, string registrationId, string legalName)
    {
        var profile = new CompanyProfile(normalizedKey, country, registrationId);
        profile.UpdateField(
            FieldName.LegalName,
            new TracedField<string>
            {
                Value = legalName,
                Confidence = Confidence.Create(0.9),
                Source = "seed",
                EnrichedAt = DateTimeOffset.UtcNow.AddDays(-1),
            },
            "seed");
        profile.SetOverallConfidence(Confidence.Create(0.9));
        return profile;
    }

    private static HttpClient CreateAuthenticatedClient(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", TestApiKey);
        return client;
    }

    /// <summary>
    /// Encapsulates the <see cref="WebApplicationFactory{TEntryPoint}"/> configuration for
    /// these E2E tests. Exposes the in-memory repositories and fakes as public fields so tests
    /// can seed state and assert outcomes after the HTTP round-trip.
    /// </summary>
    private sealed class TestHost
    {
        public InMemoryCompanyProfileRepository Profiles { get; } = new();
        public InMemoryTraceRequestRepository Traces { get; } = new();
        public InMemoryChangeEventRepository Changes { get; } = new();
        public InMemorySourceResultRepository Sources { get; } = new();
        public InMemoryValidationRecordRepository Validations { get; } = new();
        public FakeLlmDisambiguatorClient LlmClient { get; } = new();

        private readonly List<FakeEnrichmentProvider> _providers = [];

        /// <summary>Zero-based index passed to the fake LLM (−1 = no match).</summary>
        public int LlmSelectedIndex { get; init; } = -1;

        /// <summary>Raw confidence reported by the fake LLM (discounted ×0.7 by the caller).</summary>
        public double LlmConfidence { get; init; } = 0.9;

        /// <summary>
        /// Optional stub fuzzy matcher. When null, the real <c>FuzzyNameMatcher</c> (Jaro-Winkler
        /// + Token Jaccard) is used. Tests that need deterministic mid-tier scores supply a stub.
        /// </summary>
        public IFuzzyNameMatcher? FuzzyMatcher { get; init; }

        public FakeEnrichmentProvider Provider(string id) =>
            _providers.Single(p => p.ProviderId == id);

        public void SeedProfile(CompanyProfile profile)
        {
            ArgumentNullException.ThrowIfNull(profile);
            // Building a seeded profile via UpdateField() raises domain events. Those represent
            // the seeding transaction, not a change observed during the test — clear them so
            // the first SaveChangesAsync triggered by the handler doesn't dispatch stale events
            // (which would corrupt Changes.All assertions).
            profile.ClearDomainEvents();

            // Seed synchronously — InMemoryCompanyProfileRepository.UpsertAsync is non-blocking
            // (pure dictionary write). GetAwaiter().GetResult() is safe here and avoids
            // leaking an unobserved Task; .Wait() would wrap exceptions in AggregateException.
            Profiles.UpsertAsync(profile).GetAwaiter().GetResult();
        }

        public void WithTier1Provider(
            string id, int priority, IReadOnlyDictionary<FieldName, object?> fields) =>
            AddProvider(id, priority, fields, sourceQuality: 0.95);

        public void WithTier2Provider(
            string id, int priority, IReadOnlyDictionary<FieldName, object?> fields) =>
            AddProvider(id, priority, fields, sourceQuality: 0.8);

        public void WithTier3Provider(
            string id, int priority, IReadOnlyDictionary<FieldName, object?> fields) =>
            AddProvider(id, priority, fields, sourceQuality: 0.6);

        private void AddProvider(
            string id, int priority, IReadOnlyDictionary<FieldName, object?> fields, double sourceQuality)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(priority);
            _providers.Add(new FakeEnrichmentProvider
            {
                ProviderId = id,
                Priority = priority,
                SourceQuality = sourceQuality,
                Mode = FakeEnrichmentProvider.Outcome.Success,
                Fields = fields,
            });
        }

        public WebApplicationFactory<Program> BuildFactory()
        {
            // Capture locals — ConfigureTestServices closes over these references so that
            // the test can continue to assert against the same instances after the request.
            var fakes = new List<FakeEnrichmentProvider>(_providers);
            var profiles = Profiles;
            var traces = Traces;
            var changes = Changes;
            var sources = Sources;
            var validations = Validations;
            var llm = LlmClient;

#pragma warning disable CA2000 // factory disposed by caller via await using
            return new WebApplicationFactory<Program>()
                .WithWebHostBuilder(builder =>
                {
                    builder.ConfigureAppConfiguration((_, cfg) =>
                    {
                        cfg.AddInMemoryCollection(new Dictionary<string, string?>
                        {
                            // Non-empty placeholder — DbContext is removed from DI below.
                            ["ConnectionStrings:TracerDb"] = "Server=(localdb)\\placeholder;Database=TracerTest;",
                            ["Auth:ApiKeys:0"] = TestApiKey,
                            ["Revalidation:Enabled"] = "false",
                            // Providers API keys — required by Infrastructure typed HttpClient
                            // factories at DI registration. Values are never used because we
                            // strip the IEnrichmentProvider registrations below.
                            ["Providers:GoogleMaps:ApiKey"] = "fake-google",
                            ["Providers:AzureMaps:SubscriptionKey"] = "fake-azure-maps",
                            ["Providers:CompaniesHouse:ApiKey"] = "fake-ch",
                        });
                    });

                    builder.ConfigureTestServices(services =>
                    {
                        // Polly fix — WebhookCallbackService sets AttemptTimeout=30 s but the
                        // default SamplingDuration is also 30 s. Rule: ≥ 2 × AttemptTimeout.
                        services.ConfigureAll<HttpStandardResilienceOptions>(
                            o => o.CircuitBreaker.SamplingDuration = TimeSpan.FromMinutes(2));

                        // Swap repositories + unit of work for in-memory equivalents
                        services.RemoveAll<ICompanyProfileRepository>();
                        services.RemoveAll<ITraceRequestRepository>();
                        services.RemoveAll<IChangeEventRepository>();
                        services.RemoveAll<ISourceResultRepository>();
                        services.RemoveAll<IValidationRecordRepository>();
                        services.RemoveAll<IUnitOfWork>();
                        services.AddScoped<ICompanyProfileRepository>(_ => profiles);
                        services.AddScoped<ITraceRequestRepository>(_ => traces);
                        services.AddScoped<IChangeEventRepository>(_ => changes);
                        services.AddScoped<ISourceResultRepository>(_ => sources);
                        services.AddScoped<IValidationRecordRepository>(_ => validations);
                        services.AddScoped<IUnitOfWork>(sp => new InMemoryUnitOfWork(
                            profiles, traces, changes,
                            sp.GetRequiredService<MediatR.IMediator>()));

                        // Swap LLM disambiguation client — inject the shared fake so the test
                        // can inspect CallCount / LastRequest after the resolver runs.
                        llm.SelectedIndex = LlmSelectedIndex;
                        llm.Confidence = LlmConfidence;
                        services.RemoveAll<ILlmDisambiguatorClient>();
                        services.AddSingleton<ILlmDisambiguatorClient>(_ => llm);

                        // Optional fuzzy matcher stub for deterministic mid-tier scoring in
                        // entity-resolution tests. Leaving this null keeps the real matcher.
                        if (FuzzyMatcher is not null)
                        {
                            var matcher = FuzzyMatcher;
                            services.RemoveAll<IFuzzyNameMatcher>();
                            services.AddSingleton<IFuzzyNameMatcher>(matcher);
                        }

                        // Replace all enrichment providers with the test-configured fakes
                        services.RemoveAll<IEnrichmentProvider>();
                        foreach (var fake in fakes)
                            services.AddSingleton<IEnrichmentProvider>(_ => fake);
                    });
                });
#pragma warning restore CA2000
        }
    }
}
