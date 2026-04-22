using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Tracer.Infrastructure.Providers.LatamRegistry.Adapters;

namespace Tracer.Infrastructure.Providers.LatamRegistry.Providers;

/// <summary>
/// Enrichment provider for Colombian companies via the RUES (Registro Único
/// Empresarial y Social) lookup.
/// <para>
/// Priority 200 — Tier 2 (Registry). Requires at least <see cref="Domain.Enums.TraceDepth.Standard"/>.
/// Matches by country code <c>CO</c> or by 8–10 digit NIT format.
/// </para>
/// </summary>
internal sealed partial class ColombiaRuesProvider : LatamRegistryProviderBase
{
    public ColombiaRuesProvider(ILatamRegistryClient client, ILogger<ColombiaRuesProvider> logger)
        : base(client, logger) { }

    public override string ProviderId => "latam-rues";
    protected override string CountryCode => "CO";
    protected override string GenericErrorMessage => "RUES consulta failed";

    protected override bool IsPossibleCountryIdentifier(string identifier)
    {
        var trimmed = identifier.Trim();
        // Colombian NIT: 8–10 digits (with optional dash + verifier). Rejected if
        // any non-digit/dash chars are present (to avoid matching RFC / RUT).
        return NitRegex().IsMatch(trimmed);
    }

    protected override string NormalizeStatus(string status) =>
        ColombiaRuesAdapter.NormalizeStatus(status);

    [GeneratedRegex(@"^\d{8,10}(-\d)?$")]
    private static partial Regex NitRegex();
}
