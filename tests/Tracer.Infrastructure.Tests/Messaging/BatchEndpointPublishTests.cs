using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Tracer.Application.DTOs;
using Tracer.Application.Messaging;
using Tracer.Contracts.Messages;
using Tracer.Domain.Enums;
using Tracer.Domain.Interfaces;

namespace Tracer.Infrastructure.Tests.Messaging;

/// <summary>
/// Integration tests for <c>POST /api/trace/batch</c> that verify the full HTTP path:
/// endpoint → <see cref="Tracer.Application.Commands.SubmitBatchTrace.SubmitBatchTraceHandler"/>
/// → <see cref="IServiceBusPublisher"/>.
///
/// Uses <see cref="WebApplicationFactory{TEntryPoint}"/> with mocked repositories and a mocked
/// <see cref="IServiceBusPublisher"/> so no real infrastructure is required.
/// </summary>
public sealed class BatchEndpointPublishTests
{
    // Creates a fresh factory per test to ensure isolation.
    // Program is internal; WebApplicationFactory<Program> is used directly (no subclass)
    // to avoid C# accessibility errors (CS9338).
    private const string TestApiKey = "test-api-key-batch";

    // The API serializes enums as strings via JsonStringEnumConverter (registered in Program.cs).
    // Use matching options when deserializing response bodies.
    private static readonly JsonSerializerOptions ApiJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private static (WebApplicationFactory<Program> Factory, IServiceBusPublisher MockPublisher) CreateFactory()
    {
        var mockPublisher = Substitute.For<IServiceBusPublisher>();
        var mockTraceRepo = Substitute.For<ITraceRequestRepository>();
        var mockUow = Substitute.For<IUnitOfWork>();

        #pragma warning disable CA2000 // factory disposed by caller via await using
        var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, config) =>
                {
                    // Provide a non-empty fake connection string so Program.cs does not throw.
                    // The real DbContext is replaced below — this value is never used for a connection.
                    // Auth:ApiKeys:0 sets a test API key so ApiKeyAuthMiddleware does not return 401.
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:TracerDb"] = "Server=(localdb)\\mssqllocaldb;Database=TracerTest;",
                        ["Auth:ApiKeys:0"] = TestApiKey,
                        // No ServiceBus connection string → ServiceBusConsumer is NOT registered.
                    });
                });

                builder.ConfigureTestServices(services =>
                {
                    // Fix Polly startup validation: WebhookCallbackService sets AttemptTimeout=30s
                    // but the default circuit-breaker SamplingDuration is also 30s.
                    // Rule: SamplingDuration >= 2 × AttemptTimeout → needs at least 60s.
                    services.ConfigureAll<Microsoft.Extensions.Http.Resilience.HttpStandardResilienceOptions>(
                        options => options.CircuitBreaker.SamplingDuration = TimeSpan.FromMinutes(2));

                    // Replace database-backed repositories and unit of work with in-memory mocks.
                    // This avoids a real SQL Server connection for batch handler tests.
                    services.AddScoped(_ => mockTraceRepo);
                    services.AddScoped(_ => mockUow);

                    // Replace IServiceBusPublisher (registered as Singleton by AddInfrastructure).
                    var sbDescriptor = services.SingleOrDefault(
                        d => d.ServiceType == typeof(IServiceBusPublisher));
                    if (sbDescriptor is not null)
                        services.Remove(sbDescriptor);
                    services.AddSingleton(mockPublisher);
                });
            });
        #pragma warning restore CA2000

        return (factory, mockPublisher);
    }

    // ── Happy-path ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task PostBatch_SingleValidItem_Returns202Accepted()
    {
        var (factory, _) = CreateFactory();
        await using (factory.ConfigureAwait(true))
        {
            using var client = CreateAuthenticatedClient(factory);
            var items = new[] { BuildItem("ACME s.r.o.", "CZ") };

            var response = await client.PostAsJsonAsync("/api/trace/batch", items).ConfigureAwait(true);

            response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        }
    }

    [Fact]
    public async Task PostBatch_SingleValidItem_EnqueuesOneServiceBusMessage()
    {
        var (factory, mockPublisher) = CreateFactory();
        await using (factory.ConfigureAwait(true))
        {
            using var client = CreateAuthenticatedClient(factory);
            var items = new[] { BuildItem("ACME s.r.o.", "CZ") };

            await client.PostAsJsonAsync("/api/trace/batch", items).ConfigureAwait(true);

            await mockPublisher.Received(1)
                .EnqueueTraceRequestAsync(Arg.Any<TraceRequestMessage>(), Arg.Any<CancellationToken>())
                .ConfigureAwait(true);
        }
    }

    [Fact]
    public async Task PostBatch_ThreeItems_EnqueuesThreeMessages()
    {
        var (factory, mockPublisher) = CreateFactory();
        await using (factory.ConfigureAwait(true))
        {
            using var client = CreateAuthenticatedClient(factory);
            var items = new[]
            {
                BuildItem("ACME s.r.o.", "CZ"),
                BuildItem("Škoda Auto a.s.", "CZ"),
                BuildItem("BHP Group Limited", "AU"),
            };

            await client.PostAsJsonAsync("/api/trace/batch", items).ConfigureAwait(true);

            await mockPublisher.Received(3)
                .EnqueueTraceRequestAsync(Arg.Any<TraceRequestMessage>(), Arg.Any<CancellationToken>())
                .ConfigureAwait(true);
        }
    }

    [Fact]
    public async Task PostBatch_SingleItem_ResponseBodyContainsQueuedStatus()
    {
        var (factory, _) = CreateFactory();
        await using (factory.ConfigureAwait(true))
        {
            using var client = CreateAuthenticatedClient(factory);
            var items = new[] { BuildItem("Test Co.", "GB") };

            var response = await client.PostAsJsonAsync("/api/trace/batch", items).ConfigureAwait(true);
            var body = await response.Content.ReadFromJsonAsync<BatchTraceResultDto>(ApiJsonOptions)
                .ConfigureAwait(true);

            body.Should().NotBeNull();
            body!.Items.Should().HaveCount(1);
            body.Items.Single().Status.Should().Be(TraceStatus.Queued);
            body.Items.Single().TraceId.Should().NotBeEmpty();
        }
    }

    [Fact]
    public async Task PostBatch_ItemWithCorrelationId_EchoesCorrelationIdInResponse()
    {
        var (factory, _) = CreateFactory();
        await using (factory.ConfigureAwait(true))
        {
            using var client = CreateAuthenticatedClient(factory);
            const string callerCorrelationId = "ff-req-00042";
            var items = new[] { BuildItem("ACME s.r.o.", "CZ", callerCorrelationId) };

            var response = await client.PostAsJsonAsync("/api/trace/batch", items).ConfigureAwait(true);
            var body = await response.Content.ReadFromJsonAsync<BatchTraceResultDto>(ApiJsonOptions)
                .ConfigureAwait(true);

            body!.Items.Single().CorrelationId.Should().Be(callerCorrelationId);
        }
    }

    [Fact]
    public async Task PostBatch_ServiceBusMessageCorrelationId_EqualsTraceId()
    {
        var (factory, mockPublisher) = CreateFactory();
        await using (factory.ConfigureAwait(true))
        {
            using var client = CreateAuthenticatedClient(factory);
            var items = new[] { BuildItem("ACME s.r.o.", "CZ") };

            TraceRequestMessage? capturedMessage = null;
            await mockPublisher
                .EnqueueTraceRequestAsync(
                    Arg.Do<TraceRequestMessage>(m => capturedMessage = m),
                    Arg.Any<CancellationToken>())
                .ConfigureAwait(true);

            var response = await client.PostAsJsonAsync("/api/trace/batch", items).ConfigureAwait(true);
            var body = await response.Content.ReadFromJsonAsync<BatchTraceResultDto>(ApiJsonOptions)
                .ConfigureAwait(true);

            var traceId = body!.Items.Single().TraceId;
            capturedMessage.Should().NotBeNull();
            capturedMessage!.CorrelationId.Should().Be(traceId.ToString(),
                "batch handler uses TraceId as CorrelationId for SB request-reply matching");
        }
    }

    // ── Validation ────────────────────────────────────────────────────────────

    [Fact]
    public async Task PostBatch_EmptyArray_Returns422Or400ValidationError()
    {
        var (factory, _) = CreateFactory();
        await using (factory.ConfigureAwait(true))
        {
            using var client = CreateAuthenticatedClient(factory);
            var items = Array.Empty<TraceRequestDto>();

            var response = await client.PostAsJsonAsync("/api/trace/batch", items).ConfigureAwait(true);

            // FluentValidation rejects empty batches via ValidationBehavior → ValidationException
            // → endpoint catches it and returns 422 (ValidationProblem) or 400 (BadRequest).
            response.StatusCode.Should().BeOneOf(
                HttpStatusCode.UnprocessableEntity,
                HttpStatusCode.BadRequest);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static HttpClient CreateAuthenticatedClient(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", TestApiKey);
        return client;
    }

    private static TraceRequestDto BuildItem(
        string companyName, string? country = null, string? correlationId = null) =>
        new()
        {
            CompanyName = companyName,
            Country = country,
            CorrelationId = correlationId,
        };
}
