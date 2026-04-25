using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Tracer.Infrastructure.Providers.LatamRegistry.Adapters;

namespace Tracer.Infrastructure.Providers.LatamRegistry.Providers;

/// <summary>
/// Enrichment provider for Argentinian companies via AFIP's public
/// "Constancia de Inscripción" lookup.
/// <para>
/// Priority 200 — Tier 2 (Registry). Requires at least <see cref="Domain.Enums.TraceDepth.Standard"/>.
/// Matches by country code <c>AR</c> or by 11-digit CUIT format.
/// </para>
/// </summary>
internal sealed partial class ArgentinaAfipProvider : LatamRegistryProviderBase
{
    public ArgentinaAfipProvider(ILatamRegistryClient client, ILogger<ArgentinaAfipProvider> logger)
        : base(client, logger) { }

    public override string ProviderId => "latam-afip";
    protected override string CountryCode => "AR";
    protected override string GenericErrorMessage => "AFIP Constancia lookup failed";

    protected override bool IsPossibleCountryIdentifier(string identifier)
    {
        // CUIT: 11 digits with only digits/dashes/dots/spaces as separators.
        // Reject identifiers containing letters (e.g. RFC "WMT970714R10") so we
        // don't shadow the Mexico adapter's fallback match.
        var trimmed = identifier.Trim();
        if (ContainsLetterRegex().IsMatch(trimmed))
            return false;

        var digits = NonDigitsRegex().Replace(trimmed, string.Empty);
        return digits.Length == 11;
    }

    protected override string NormalizeStatus(string status) =>
        ArgentinaAfipAdapter.NormalizeStatus(status);

    [GeneratedRegex(@"\D")]
    private static partial Regex NonDigitsRegex();

    [GeneratedRegex(@"[A-Za-z]")]
    private static partial Regex ContainsLetterRegex();
}
