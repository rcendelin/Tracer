using System.Text.RegularExpressions;
using AngleSharp;
using AngleSharp.Dom;

namespace Tracer.Infrastructure.Providers.LatamRegistry.Adapters;

/// <summary>
/// Adapter for Mexico's SAT (Servicio de Administración Tributaria) "Constancia de
/// Situación Fiscal" endpoint. Accepts an RFC (Registro Federal de Contribuyentes) —
/// 13 alphanumeric characters for individuals, 12 for legal persons.
/// <para>
/// Note: the SAT portal protects most endpoints with a CAPTCHA; this adapter runs
/// best-effort against the anonymously reachable informational page. When the page
/// returns a CAPTCHA wall the parser returns <see langword="null"/> (treated as
/// <c>NotFound</c>) rather than throwing — that matches the block's "limited
/// availability" scope per the B-89 plan.
/// </para>
/// </summary>
internal sealed partial class MexicoSatAdapter : ILatamRegistryAdapter
{
    public string CountryCode => "MX";
    public string BaseUrl => "https://siat.sat.gob.mx/";

    /// <inheritdoc />
    public string? NormalizeIdentifier(string identifier)
    {
        var upper = identifier.Trim().ToUpperInvariant();
        // RFC: 12 chars (legal person) or 13 chars (individual); alphanumeric.
        return RfcRegex().IsMatch(upper) ? upper : null;
    }

    /// <inheritdoc />
    public HttpRequestMessage BuildLookupRequest(string normalizedIdentifier)
    {
        // SAT's informational endpoint accepts the RFC via querystring; when the
        // portal requires a CAPTCHA the response still returns HTML — Parse()
        // treats those pages as no-match.
        var uri = new Uri(
            new Uri(BaseUrl),
            $"app/seg/SolicitudConstancia.aspx?rfc={Uri.EscapeDataString(normalizedIdentifier)}");
        return new HttpRequestMessage(HttpMethod.Get, uri);
    }

    /// <inheritdoc />
    public LatamRegistrySearchResult? Parse(string body, string normalizedIdentifier)
    {
        // Quick detection of CAPTCHA / login walls — the constancia requires a
        // CAPTCHA token so public calls almost always hit this path.
        if (LooksLikeCaptchaWall(body))
            return null;

        var config = Configuration.Default;
        var context = BrowsingContext.New(config);
#pragma warning disable CA1849 // AngleSharp Content() parsing is synchronous — no I/O
        var document = context.OpenAsync(req => req.Content(body)).GetAwaiter().GetResult();
#pragma warning restore CA1849

        var labelled = ExtractLabelledFields(document);

        if (!labelled.TryGetValue("Denominación o Razón Social", out var name)
            && !labelled.TryGetValue("Denominación/Razón Social", out name)
            && !labelled.TryGetValue("Nombre", out name))
        {
            return null;
        }

        labelled.TryGetValue("Estatus del Contribuyente", out var status);
        labelled.TryGetValue("Régimen", out var entityType);
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
    /// Normalizes SAT status strings to canonical English.
    /// SAT uses uppercase labels: <c>ACTIVO</c>, <c>SUSPENDIDO</c>, <c>CANCELADO</c>, <c>INACTIVO</c>.
    /// </summary>
    internal static string NormalizeStatus(string status) =>
        status.Trim() switch
        {
            var s when s.Equals("ACTIVO", StringComparison.OrdinalIgnoreCase)
                || s.Equals("Activo", StringComparison.OrdinalIgnoreCase) => "active",
            var s when s.Equals("INACTIVO", StringComparison.OrdinalIgnoreCase)
                || s.Equals("Inactivo", StringComparison.OrdinalIgnoreCase) => "inactive",
            var s when s.Equals("SUSPENDIDO", StringComparison.OrdinalIgnoreCase)
                || s.Equals("Suspendido", StringComparison.OrdinalIgnoreCase) => "suspended",
            var s when s.Equals("CANCELADO", StringComparison.OrdinalIgnoreCase)
                || s.Equals("Cancelado", StringComparison.OrdinalIgnoreCase) => "dissolved",
            _ => status.Trim(),
        };

    private static bool LooksLikeCaptchaWall(string body) =>
        body.Contains("captcha", StringComparison.OrdinalIgnoreCase)
        || body.Contains("valide que no es un robot", StringComparison.OrdinalIgnoreCase)
        || body.Contains("verificación de imagen", StringComparison.OrdinalIgnoreCase);

    private static Dictionary<string, string> ExtractLabelledFields(IDocument document)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // SAT renders fields as <td class="label"> + <td class="value"> inside a <table>.
        var rows = document.QuerySelectorAll("tr");
        foreach (var row in rows)
        {
            var cells = row.QuerySelectorAll("td, th");
            if (cells.Length < 2) continue;

            var label = cells[0].TextContent?.Trim().TrimEnd(':');
            var value = cells[1].TextContent?.Trim();
            if (!string.IsNullOrWhiteSpace(label) && !string.IsNullOrWhiteSpace(value)
                && !result.ContainsKey(label!))
                result[label!] = value!;
        }

        // Definition-list fallback.
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

    [GeneratedRegex(@"^[A-Z&Ñ]{3,4}\d{6}[A-Z\d]{3}$")]
    private static partial Regex RfcRegex();
}
