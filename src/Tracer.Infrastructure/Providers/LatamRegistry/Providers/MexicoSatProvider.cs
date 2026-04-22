using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Tracer.Infrastructure.Providers.LatamRegistry.Adapters;

namespace Tracer.Infrastructure.Providers.LatamRegistry.Providers;

/// <summary>
/// Enrichment provider for Mexican companies via SAT's "Constancia de Situación
/// Fiscal" endpoint.
/// <para>
/// Priority 200 — Tier 2 (Registry). Requires at least <see cref="Domain.Enums.TraceDepth.Standard"/>.
/// Matches by country code <c>MX</c> or by 12/13-character RFC format.
/// </para>
/// <para>
/// The SAT portal is CAPTCHA-protected; when the endpoint returns a CAPTCHA wall
/// the adapter returns <see langword="null"/> and this provider reports
/// <see cref="Domain.Interfaces.SourceStatus.NotFound"/> rather than an error —
/// matching the B-89 plan's "limited availability" scope.
/// </para>
/// </summary>
internal sealed partial class MexicoSatProvider : LatamRegistryProviderBase
{
    public MexicoSatProvider(ILatamRegistryClient client, ILogger<MexicoSatProvider> logger)
        : base(client, logger) { }

    public override string ProviderId => "latam-sat";
    protected override string CountryCode => "MX";
    protected override string GenericErrorMessage => "SAT Constancia lookup failed";

    protected override bool IsPossibleCountryIdentifier(string identifier) =>
        RfcRegex().IsMatch(identifier.Trim().ToUpperInvariant());

    protected override string NormalizeStatus(string status) =>
        MexicoSatAdapter.NormalizeStatus(status);

    [GeneratedRegex(@"^[A-Z&Ñ]{3,4}\d{6}[A-Z\d]{3}$")]
    private static partial Regex RfcRegex();
}
