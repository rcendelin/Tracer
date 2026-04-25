using System.Collections.Immutable;
using Tracer.Domain.Enums;
using Tracer.Domain.Interfaces;

namespace Tracer.Infrastructure.Tests.Providers.LatamRegistry;

/// <summary>
/// Shared <see cref="TraceContext"/> factory for LATAM provider tests.
/// Keeps the four per-country test classes from duplicating a near-identical
/// Create helper and TraceRequest constructor argument list.
/// </summary>
internal static class LatamProviderTestContext
{
    /// <summary>
    /// Builds a <see cref="TraceContext"/> suitable for provider unit tests.
    /// Defaults to <see cref="TraceDepth.Standard"/>, a dummy company name, and
    /// an empty accumulated-fields set.
    /// </summary>
    public static TraceContext Create(
        string? country,
        string? registrationId = null,
        string? taxId = null,
        string? companyName = "Test Co",
        TraceDepth depth = TraceDepth.Standard,
        IReadOnlySet<FieldName>? accumulated = null) =>
        new()
        {
            Request = new Domain.Entities.TraceRequest(
                companyName: companyName,
                phone: null, email: null, website: null, address: null, city: null,
                country: country,
                registrationId: registrationId,
                taxId: taxId,
                industryHint: null,
                depth: depth,
                callbackUrl: null,
                source: "test"),
            AccumulatedFields = accumulated ?? ImmutableHashSet<FieldName>.Empty,
        };
}
