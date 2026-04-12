using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Tracer.Domain.Enums;
using Tracer.Domain.Interfaces;
using Tracer.Domain.ValueObjects;

namespace Tracer.Infrastructure.Providers.BrazilCnpj;

/// <summary>
/// Enrichment provider for Brazilian company data via the BrasilAPI CNPJ endpoint.
/// Queries the Brazilian Federal Revenue Service (Receita Federal) data through BrasilAPI.
/// <para>
/// Priority 200 — Tier 2 (Registry API). Runs sequentially after Tier 1 providers.
/// Requires at least <see cref="TraceDepth.Standard"/> depth.
/// </para>
/// <para>
/// CanHandle logic:
/// <list type="number">
///   <item>Country is "BR" (Brazil) — primary match</item>
///   <item>RegistrationId matches CNPJ format (14 digits, with or without formatting) — fallback</item>
/// </list>
/// </para>
/// <para>
/// Note: BrasilAPI only supports CNPJ lookup — no name search is available.
/// The provider requires a CNPJ to be present in the request or the existing profile.
/// </para>
/// </summary>
internal sealed partial class BrazilCnpjProvider : IEnrichmentProvider
{
    private readonly IBrazilCnpjClient _client;
    private readonly ILogger<BrazilCnpjProvider> _logger;

    public BrazilCnpjProvider(IBrazilCnpjClient client, ILogger<BrazilCnpjProvider> logger)
    {
        _client = client;
        _logger = logger;
    }

    public string ProviderId => "brazil-cnpj";
    public int Priority => 200;
    public double SourceQuality => 0.90;

    /// <inheritdoc />
    public bool CanHandle(TraceContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Depth < TraceDepth.Standard)
            return false;

        // Must have a CNPJ (BrasilAPI doesn't support name search)
        if (string.IsNullOrWhiteSpace(context.Request.RegistrationId))
            return false;

        // Brazilian company by country code
        if (string.Equals(context.Country, "BR", StringComparison.OrdinalIgnoreCase))
            return true;

        // Registration ID matches CNPJ format (14 digits, optionally formatted)
        var normalized = BrazilCnpjClient.NormalizeCnpj(context.Request.RegistrationId);
        if (CnpjRegex().IsMatch(normalized))
            return true;

        return false;
    }

    /// <inheritdoc />
    public async Task<ProviderResult> EnrichAsync(TraceContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var cnpj = context.Request.RegistrationId;

            if (string.IsNullOrWhiteSpace(cnpj))
            {
                LogNoIdentifier();
                return ProviderResult.NotFound(stopwatch.Elapsed);
            }

            var result = await _client.GetByCnpjAsync(cnpj, cancellationToken).ConfigureAwait(false);

            if (result is null)
                return ProviderResult.NotFound(stopwatch.Elapsed);

            var fields = MapToFields(result);
            var rawJson = JsonSerializer.Serialize(result);

            var fieldCount = fields.Count;
            var normalizedCnpj = BrazilCnpjClient.NormalizeCnpj(cnpj);
            LogEnrichmentSuccess(normalizedCnpj, fieldCount);

            return ProviderResult.Success(fields, stopwatch.Elapsed, rawJson);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ProviderResult.Timeout(stopwatch.Elapsed);
        }
        catch (HttpRequestException ex)
        {
            LogEnrichmentError(ex);
            return ProviderResult.Error("BrasilAPI CNPJ call failed", stopwatch.Elapsed);
        }
    }

    // ── Field mapping ────────────────────────────────────────────────────────

    private static Dictionary<FieldName, object?> MapToFields(BrazilCnpjResponse response)
    {
        var fields = new Dictionary<FieldName, object?>();

        if (!string.IsNullOrWhiteSpace(response.RazaoSocial))
            fields[FieldName.LegalName] = response.RazaoSocial;

        if (!string.IsNullOrWhiteSpace(response.NomeFantasia))
            fields[FieldName.TradeName] = response.NomeFantasia;

        if (!string.IsNullOrWhiteSpace(response.Cnpj))
            fields[FieldName.RegistrationId] = FormatCnpj(response.Cnpj);

        if (!string.IsNullOrWhiteSpace(response.NaturezaJuridica))
            fields[FieldName.LegalForm] = response.NaturezaJuridica;

        if (!string.IsNullOrWhiteSpace(response.DescricaoSituacaoCadastral))
            fields[FieldName.EntityStatus] = NormalizeStatus(response.DescricaoSituacaoCadastral);

        if (!string.IsNullOrWhiteSpace(response.CnaeFiscalDescricao))
            fields[FieldName.Industry] = response.CnaeFiscalDescricao;

        if (HasMeaningfulAddress(response))
            fields[FieldName.RegisteredAddress] = MapAddress(response);

        if (!string.IsNullOrWhiteSpace(response.DddTelefone1))
            fields[FieldName.Phone] = FormatBrazilPhone(response.DddTelefone1);

        if (!string.IsNullOrWhiteSpace(response.Email))
            fields[FieldName.Email] = response.Email.Trim();

        return fields;
    }

    private static Address MapAddress(BrazilCnpjResponse response)
    {
        var street = BuildStreetLine(response.Logradouro, response.Numero, response.Complemento);

        return new Address
        {
            Street = street ?? string.Empty,
            City = response.Municipio ?? string.Empty,
            PostalCode = FormatCep(response.Cep),
            Region = response.Uf,
            Country = "BR",
        };
    }

    private static bool HasMeaningfulAddress(BrazilCnpjResponse response) =>
        !string.IsNullOrWhiteSpace(response.Logradouro)
        || !string.IsNullOrWhiteSpace(response.Municipio);

    private static string? BuildStreetLine(string? street, string? number, string? complement)
    {
        if (string.IsNullOrWhiteSpace(street))
            return null;

        var line = street.Trim();

        if (!string.IsNullOrWhiteSpace(number))
            line += ", " + number.Trim();

        if (!string.IsNullOrWhiteSpace(complement))
            line += " - " + complement.Trim();

        return line;
    }

    /// <summary>
    /// Formats a raw CNPJ string (14 digits) into the standard Brazilian format: XX.XXX.XXX/XXXX-XX.
    /// </summary>
    private static string FormatCnpj(string cnpj)
    {
        var normalized = BrazilCnpjClient.NormalizeCnpj(cnpj);
        if (normalized.Length != 14)
            return cnpj;

        return string.Create(18, normalized, static (span, n) =>
        {
            span[0] = n[0]; span[1] = n[1]; span[2] = '.';
            span[3] = n[2]; span[4] = n[3]; span[5] = n[4]; span[6] = '.';
            span[7] = n[5]; span[8] = n[6]; span[9] = n[7]; span[10] = '/';
            span[11] = n[8]; span[12] = n[9]; span[13] = n[10]; span[14] = n[11]; span[15] = '-';
            span[16] = n[12]; span[17] = n[13];
        });
    }

    /// <summary>
    /// Formats a CEP (Brazilian postal code) as XXXXX-XXX.
    /// </summary>
    private static string FormatCep(string? cep)
    {
        if (string.IsNullOrWhiteSpace(cep))
            return string.Empty;

        var digits = CepDigitsRegex().Replace(cep.Trim(), string.Empty);
        if (digits.Length == 8)
            return $"{digits[..5]}-{digits[5..]}";

        return cep.Trim();
    }

    /// <summary>
    /// Formats a Brazilian phone number. BrasilAPI returns it as concatenated DDD+number (e.g. "2132242164").
    /// Converts to "+55 (21) 3224-2164" format.
    /// </summary>
    private static string FormatBrazilPhone(string rawPhone)
    {
        var digits = CepDigitsRegex().Replace(rawPhone.Trim(), string.Empty);

        // DDD (2 digits) + number (8-9 digits)
        if (digits.Length >= 10 && digits.Length <= 11)
        {
            var ddd = digits[..2];
            var number = digits[2..];
            var formatted = number.Length == 9
                ? $"{number[..5]}-{number[5..]}"
                : $"{number[..4]}-{number[4..]}";
            return $"+55 ({ddd}) {formatted}";
        }

        return rawPhone.Trim();
    }

    /// <summary>
    /// Normalizes Portuguese registration status to canonical English for consistent CKB storage.
    /// Uses <see cref="StringComparison.OrdinalIgnoreCase"/> to satisfy CA1308 (no ToLowerInvariant).
    /// </summary>
    internal static string NormalizeStatus(string portugueseStatus) =>
        portugueseStatus.Trim() switch
        {
            var s when s.Equals("ATIVA", StringComparison.OrdinalIgnoreCase) => "active",
            var s when s.Equals("INAPTA", StringComparison.OrdinalIgnoreCase) => "inactive",
            var s when s.Equals("SUSPENSA", StringComparison.OrdinalIgnoreCase) => "suspended",
            var s when s.Equals("BAIXADA", StringComparison.OrdinalIgnoreCase) => "dissolved",
            var s when s.Equals("NULA", StringComparison.OrdinalIgnoreCase) => "annulled",
            _ => portugueseStatus.Trim(),
        };

    // ── Regex ────────────────────────────────────────────────────────────────

    /// <summary>Matches exactly 14 digits (normalized CNPJ).</summary>
    [GeneratedRegex(@"^\d{14}$")]
    private static partial Regex CnpjRegex();

    /// <summary>Matches non-digit characters for stripping.</summary>
    [GeneratedRegex(@"\D")]
    private static partial Regex CepDigitsRegex();

    // ── Logging ──────────────────────────────────────────────────────────────

    [LoggerMessage(Level = LogLevel.Debug, Message = "BrazilCNPJ provider: No CNPJ available")]
    private partial void LogNoIdentifier();

    [LoggerMessage(Level = LogLevel.Information,
        Message = "BrazilCNPJ provider: Enriched CNPJ {Cnpj} with {FieldCount} fields")]
    private partial void LogEnrichmentSuccess(string cnpj, int fieldCount);

    [LoggerMessage(Level = LogLevel.Warning, Message = "BrazilCNPJ provider: Enrichment failed")]
    private partial void LogEnrichmentError(Exception ex);
}
