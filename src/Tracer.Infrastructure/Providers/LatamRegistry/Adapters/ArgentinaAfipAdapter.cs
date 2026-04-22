using System.Text.RegularExpressions;
using AngleSharp;
using AngleSharp.Dom;

namespace Tracer.Infrastructure.Providers.LatamRegistry.Adapters;

/// <summary>
/// Adapter for Argentina's AFIP "Constancia de Inscripción" public lookup.
/// Accepts a CUIT (Clave Única de Identificación Tributaria) — 11 digits, often
/// formatted as <c>XX-XXXXXXXX-X</c>.
/// </summary>
internal sealed partial class ArgentinaAfipAdapter : ILatamRegistryAdapter
{
    public string CountryCode => "AR";
    public string BaseUrl => "https://seti.afip.gob.ar/padron-puc-constancia-internet/";

    /// <inheritdoc />
    public string? NormalizeIdentifier(string identifier)
    {
        var digits = NonDigitsRegex().Replace(identifier.Trim(), string.Empty);
        return digits.Length == 11 ? digits : null;
    }

    /// <inheritdoc />
    public HttpRequestMessage BuildLookupRequest(string normalizedIdentifier)
    {
        // AFIP's public "Constancia" page accepts the CUIT as a query parameter.
        // The real portal wraps it behind a captcha for humans, but the HTML
        // returned by the server-side component is parseable for automated
        // best-effort lookups.
        var uri = new Uri(
            new Uri(BaseUrl),
            $"ConsultaConstanciaAction.do?cuit={Uri.EscapeDataString(normalizedIdentifier)}");
        return new HttpRequestMessage(HttpMethod.Get, uri);
    }

    /// <inheritdoc />
    public LatamRegistrySearchResult? Parse(string body, string normalizedIdentifier)
    {
        var config = Configuration.Default;
        var context = BrowsingContext.New(config);
#pragma warning disable CA1849 // AngleSharp's Content() parsing is synchronous — no I/O
        var document = context.OpenAsync(req => req.Content(body)).GetAwaiter().GetResult();
#pragma warning restore CA1849

        // AFIP surfaces the constancia as a labelled layout ("Razón Social", "Estado",
        // "Actividad principal"). Look for the label → value pairs regardless of the
        // exact tag type (the server HTML varies across request types).
        var labelled = ExtractLabelledFields(document);

        if (!labelled.TryGetValue("Razón Social", out var name)
            && !labelled.TryGetValue("Apellido y Nombre", out name)
            && !labelled.TryGetValue("Denominación", out name))
        {
            // Captcha wall / no-match page — best-effort parsers return null.
            return null;
        }

        labelled.TryGetValue("Estado", out var status);
        labelled.TryGetValue("Forma Jurídica", out var entityType);
        labelled.TryGetValue("Domicilio Fiscal", out var address);

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
    /// Normalizes Spanish AFIP status strings to canonical English values for CKB storage.
    /// </summary>
    internal static string NormalizeStatus(string status) =>
        status.Trim() switch
        {
            var s when s.Equals("ACTIVO", StringComparison.OrdinalIgnoreCase)
                || s.Equals("Activo", StringComparison.OrdinalIgnoreCase) => "active",
            var s when s.Equals("INACTIVO", StringComparison.OrdinalIgnoreCase) => "inactive",
            var s when s.Equals("SUSPENDIDO", StringComparison.OrdinalIgnoreCase)
                || s.Equals("Suspendido", StringComparison.OrdinalIgnoreCase) => "suspended",
            var s when s.Equals("BAJA", StringComparison.OrdinalIgnoreCase)
                || s.Equals("Baja", StringComparison.OrdinalIgnoreCase)
                || s.Equals("Cancelado", StringComparison.OrdinalIgnoreCase)
                || s.Equals("CANCELADO", StringComparison.OrdinalIgnoreCase) => "dissolved",
            _ => status.Trim(),
        };

    private static Dictionary<string, string> ExtractLabelledFields(IDocument document)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Pattern A: <dt>Label</dt><dd>Value</dd>
        var dts = document.QuerySelectorAll("dt");
        foreach (var dt in dts)
        {
            var value = dt.NextElementSibling;
            if (value is { LocalName: "dd" })
                TryAdd(result, dt.TextContent, value.TextContent);
        }

        // Pattern B: <th>Label</th><td>Value</td> within the same table row.
        var ths = document.QuerySelectorAll("tr > th");
        foreach (var th in ths)
        {
            var value = th.NextElementSibling;
            if (value is { LocalName: "td" })
                TryAdd(result, th.TextContent, value.TextContent);
        }

        // Pattern C: Colon-separated plain text inside any block element.
        var blocks = document.QuerySelectorAll("p, li, span, div");
        foreach (var block in blocks)
        {
            var text = block.TextContent;
            var colon = text.IndexOf(':', StringComparison.Ordinal);
            if (colon <= 0 || colon >= text.Length - 1)
                continue;

            var label = text[..colon].Trim();
            var value = text[(colon + 1)..].Trim();
            TryAdd(result, label, value);
        }

        return result;
    }

    private static void TryAdd(Dictionary<string, string> map, string? label, string? value)
    {
        if (string.IsNullOrWhiteSpace(label) || string.IsNullOrWhiteSpace(value))
            return;

        var key = label.Trim().TrimEnd(':');
        // First occurrence wins — avoids overwriting structured values with plain-text fallbacks.
        if (!map.ContainsKey(key))
            map[key] = value.Trim();
    }

    [GeneratedRegex(@"\D")]
    private static partial Regex NonDigitsRegex();
}
