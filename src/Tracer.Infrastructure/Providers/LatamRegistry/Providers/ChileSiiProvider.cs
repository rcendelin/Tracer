using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Tracer.Infrastructure.Providers.LatamRegistry.Adapters;

namespace Tracer.Infrastructure.Providers.LatamRegistry.Providers;

/// <summary>
/// Enrichment provider for Chilean companies via SII's "Situación Tributaria
/// de Terceros" lookup.
/// <para>
/// Priority 200 — Tier 2 (Registry). Requires at least <see cref="Domain.Enums.TraceDepth.Standard"/>.
/// Matches by country code <c>CL</c> or by RUT format
/// (6–9 digits followed by a verification digit or "K").
/// </para>
/// </summary>
internal sealed partial class ChileSiiProvider : LatamRegistryProviderBase
{
    public ChileSiiProvider(ILatamRegistryClient client, ILogger<ChileSiiProvider> logger)
        : base(client, logger) { }

    public override string ProviderId => "latam-sii";
    protected override string CountryCode => "CL";
    protected override string GenericErrorMessage => "SII Situación Tributaria lookup failed";

    protected override bool IsPossibleCountryIdentifier(string identifier) =>
        RutRegex().IsMatch(identifier.Trim());

    protected override string NormalizeStatus(string status) =>
        ChileSiiAdapter.NormalizeStatus(status);

    // RUT pattern: optional dots as thousand separators, optional dash before the
    // verifier, which is a digit or "K" / "k".
    [GeneratedRegex(@"^\d{1,3}(\.?\d{3}){1,2}-[\dKk]$|^\d{6,9}-?[\dKk]$")]
    private static partial Regex RutRegex();
}
