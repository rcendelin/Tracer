using System.Text.RegularExpressions;
using AngleSharp;
using AngleSharp.Dom;

namespace Tracer.Infrastructure.Providers.LatamRegistry.Adapters;

/// <summary>
/// Adapter for Colombia's RUES (Registro Único Empresarial y Social) consultation
/// endpoint. Accepts a NIT (Número de Identificación Tributaria) — 9 to 10 digits,
/// optionally with a dash-prefixed verification digit (<c>XXXXXXXXX-X</c>).
/// </summary>
internal sealed partial class ColombiaRuesAdapter : ILatamRegistryAdapter
{
    public string CountryCode => "CO";
    public string BaseUrl => "https://www.rues.org.co/RUES_Web/Consultas/";

    /// <inheritdoc />
    public string? NormalizeIdentifier(string identifier)
    {
        var cleaned = NonDigitsRegex().Replace(identifier.Trim(), string.Empty);
        // NIT core is 9 digits; an optional verification digit (10 total) is accepted
        // and kept (RUES lookups work with or without the verifier).
        return cleaned.Length is >= 8 and <= 10 ? cleaned : null;
    }

    /// <inheritdoc />
    public HttpRequestMessage BuildLookupRequest(string normalizedIdentifier)
    {
        // RUES exposes a consulta page that filters by NIT. A GET with query string
        // is sufficient for the anonymous preview the registry returns.
        var uri = new Uri(
            new Uri(BaseUrl),
            $"ConsultaEmpresa.aspx?nit={Uri.EscapeDataString(normalizedIdentifier)}");
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
            && !labelled.TryGetValue("Nombre", out name)
            && !labelled.TryGetValue("Empresa", out name))
        {
            return null;
        }

        labelled.TryGetValue("Estado", out var status);
        labelled.TryGetValue("Organización Jurídica", out var entityType);
        labelled.TryGetValue("Categoría Matrícula", out var categoryType);
        labelled.TryGetValue("Dirección Comercial", out var address);
        labelled.TryGetValue("Dirección", out var fallbackAddress);

        return new LatamRegistrySearchResult
        {
            EntityName = name.Trim(),
            RegistrationId = normalizedIdentifier,
            CountryCode = CountryCode,
            Status = status?.Trim(),
            EntityType = (entityType ?? categoryType)?.Trim(),
            Address = (address ?? fallbackAddress)?.Trim(),
        };
    }

    /// <summary>
    /// Normalizes RUES status strings to canonical English.
    /// RUES uses "ACTIVA", "CANCELADA", "SUSPENDIDA", "LIQUIDADA", "INACTIVA".
    /// </summary>
    internal static string NormalizeStatus(string status) =>
        status.Trim() switch
        {
            var s when s.Equals("ACTIVA", StringComparison.OrdinalIgnoreCase)
                || s.Equals("Activa", StringComparison.OrdinalIgnoreCase)
                || s.Equals("ACTIVO", StringComparison.OrdinalIgnoreCase) => "active",
            var s when s.Equals("INACTIVA", StringComparison.OrdinalIgnoreCase)
                || s.Equals("Inactiva", StringComparison.OrdinalIgnoreCase) => "inactive",
            var s when s.Equals("CANCELADA", StringComparison.OrdinalIgnoreCase)
                || s.Equals("Cancelada", StringComparison.OrdinalIgnoreCase)
                || s.Equals("LIQUIDADA", StringComparison.OrdinalIgnoreCase)
                || s.Equals("Liquidada", StringComparison.OrdinalIgnoreCase) => "dissolved",
            var s when s.Equals("SUSPENDIDA", StringComparison.OrdinalIgnoreCase)
                || s.Equals("Suspendida", StringComparison.OrdinalIgnoreCase) => "suspended",
            var s when s.Equals("EN LIQUIDACION", StringComparison.OrdinalIgnoreCase)
                || s.Equals("EN LIQUIDACIÓN", StringComparison.OrdinalIgnoreCase) => "in_liquidation",
            _ => status.Trim(),
        };

    private static Dictionary<string, string> ExtractLabelledFields(IDocument document)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // RUES pages render data as definition lists or labelled table rows.
        var rows = document.QuerySelectorAll("tr");
        foreach (var row in rows)
        {
            var cells = row.QuerySelectorAll("th, td");
            if (cells.Length < 2) continue;

            var label = cells[0].TextContent?.Trim().TrimEnd(':');
            var value = cells[1].TextContent?.Trim();
            if (!string.IsNullOrWhiteSpace(label) && !string.IsNullOrWhiteSpace(value)
                && !result.ContainsKey(label!))
                result[label!] = value!;
        }

        var dts = document.QuerySelectorAll("dt");
        foreach (var dt in dts)
        {
            var value = dt.NextElementSibling;
            if (value is not { LocalName: "dd" }) continue;
            var label = dt.TextContent?.Trim().TrimEnd(':');
            if (!string.IsNullOrWhiteSpace(label) && !string.IsNullOrWhiteSpace(value.TextContent)
                && !result.ContainsKey(label!))
                result[label!] = value.TextContent.Trim();
        }

        return result;
    }

    [GeneratedRegex(@"\D")]
    private static partial Regex NonDigitsRegex();
}
