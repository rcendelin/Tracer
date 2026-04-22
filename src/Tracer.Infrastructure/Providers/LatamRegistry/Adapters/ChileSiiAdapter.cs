using System.Text.RegularExpressions;
using AngleSharp;
using AngleSharp.Dom;

namespace Tracer.Infrastructure.Providers.LatamRegistry.Adapters;

/// <summary>
/// Adapter for Chile's SII (Servicio de Impuestos Internos) "Situación Tributaria
/// de Terceros" lookup. Accepts a RUT (Rol Único Tributario) — 7 to 9 digits plus
/// a single verification character (<c>XX.XXX.XXX-K</c> or similar).
/// </summary>
internal sealed partial class ChileSiiAdapter : ILatamRegistryAdapter
{
    public string CountryCode => "CL";
    public string BaseUrl => "https://zeus.sii.cl/cvc_cgi/stc/";

    /// <inheritdoc />
    public string? NormalizeIdentifier(string identifier)
    {
        var cleaned = NonRutCharsRegex().Replace(identifier.Trim(), string.Empty);
        if (cleaned.Length < 2)
            return null;

        var digits = cleaned[..^1];
        var verifier = char.ToUpperInvariant(cleaned[^1]);

        if (digits.Length is < 6 or > 9)
            return null;
        if (!digits.All(char.IsDigit))
            return null;
        if (!char.IsDigit(verifier) && verifier != 'K')
            return null;

        // Canonical form: NNNNNNNN-K (no dots, uppercase verifier).
        return $"{digits}-{verifier}";
    }

    /// <inheritdoc />
    public HttpRequestMessage BuildLookupRequest(string normalizedIdentifier)
    {
        // SII expects the RUT body and verifier as separate parameters
        // ("rut" + "dv") on the STC endpoint.
        var parts = normalizedIdentifier.Split('-');
        var rut = parts[0];
        var dv = parts[1];

        var uri = new Uri(
            new Uri(BaseUrl),
            $"getstc?rut={Uri.EscapeDataString(rut)}&dv={Uri.EscapeDataString(dv)}");
        return new HttpRequestMessage(HttpMethod.Get, uri);
    }

    /// <inheritdoc />
    public LatamRegistrySearchResult? Parse(string body, string normalizedIdentifier)
    {
        var config = Configuration.Default;
        var context = BrowsingContext.New(config);
#pragma warning disable CA1849 // AngleSharp Content() parsing is synchronous — no I/O
        var document = context.OpenAsync(req => req.Content(body)).GetAwaiter().GetResult();
#pragma warning restore CA1849

        var labelled = ExtractLabelledFields(document);

        if (!labelled.TryGetValue("Razón Social", out var name)
            && !labelled.TryGetValue("Nombre o Razón Social", out name)
            && !labelled.TryGetValue("Contribuyente", out name))
        {
            return null;
        }

        labelled.TryGetValue("Actividades Económicas", out var entityType);
        labelled.TryGetValue("Situación Tributaria", out var status);
        labelled.TryGetValue("Domicilio", out var address);

        return new LatamRegistrySearchResult
        {
            EntityName = name.Trim(),
            RegistrationId = normalizedIdentifier,
            CountryCode = CountryCode,
            Status = status?.Trim(),
            EntityType = entityType?.Trim(),
            Address = address?.Trim(),
        };
    }

    /// <summary>
    /// Normalizes Chilean SII status strings to canonical English.
    /// </summary>
    internal static string NormalizeStatus(string status)
    {
        var s = status.Trim();

        if (s.Contains("vigente", StringComparison.OrdinalIgnoreCase)
            || s.Contains("activo", StringComparison.OrdinalIgnoreCase)
            || s.Contains("activa", StringComparison.OrdinalIgnoreCase))
            return "active";
        if (s.Contains("término", StringComparison.OrdinalIgnoreCase)
            || s.Contains("termino", StringComparison.OrdinalIgnoreCase)
            || s.Contains("disuelta", StringComparison.OrdinalIgnoreCase)
            || s.Contains("disuelto", StringComparison.OrdinalIgnoreCase))
            return "dissolved";
        if (s.Contains("suspend", StringComparison.OrdinalIgnoreCase))
            return "suspended";

        return s;
    }

    private static Dictionary<string, string> ExtractLabelledFields(IDocument document)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // SII typically returns tabular data with <b>Label:</b> <text>value</text>.
        var bolds = document.QuerySelectorAll("b, strong");
        foreach (var bold in bolds)
        {
            var label = bold.TextContent?.Trim().TrimEnd(':');
            if (string.IsNullOrWhiteSpace(label))
                continue;

            // Value is typically the sibling text node or the next element sibling.
            var sibling = bold.NextSibling;
            string? value = null;
            while (sibling is not null)
            {
                if (!string.IsNullOrWhiteSpace(sibling.TextContent))
                {
                    value = sibling.TextContent.Trim();
                    break;
                }
                sibling = sibling.NextSibling;
            }

            if (!string.IsNullOrWhiteSpace(value) && !result.ContainsKey(label))
                result[label] = value;
        }

        // Table rows: <tr><td>Label</td><td>Value</td></tr>
        var rows = document.QuerySelectorAll("tr");
        foreach (var row in rows)
        {
            var cells = row.QuerySelectorAll("td");
            if (cells.Length < 2) continue;

            var label = cells[0].TextContent?.Trim().TrimEnd(':');
            var value = cells[1].TextContent?.Trim();
            if (!string.IsNullOrWhiteSpace(label) && !string.IsNullOrWhiteSpace(value)
                && !result.ContainsKey(label!))
                result[label!] = value!;
        }

        return result;
    }

    [GeneratedRegex(@"[^\dKk]")]
    private static partial Regex NonRutCharsRegex();
}
