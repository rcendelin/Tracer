using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Tracer.Infrastructure.Providers.BrazilCnpj;

/// <summary>
/// HTTP client for the BrasilAPI CNPJ endpoint.
/// Configured with resilience policies (retry + timeout) via <c>Microsoft.Extensions.Http.Resilience</c>.
/// <para>
/// BrasilAPI is a free, open-source API that mirrors data from the Brazilian Federal Revenue
/// Service (Receita Federal). No API key is required.
/// </para>
/// </summary>
internal sealed partial class BrazilCnpjClient : IBrazilCnpjClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<BrazilCnpjClient> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public BrazilCnpjClient(HttpClient httpClient, ILogger<BrazilCnpjClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<BrazilCnpjResponse?> GetByCnpjAsync(string cnpj, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cnpj, nameof(cnpj));

        var normalized = NormalizeCnpj(cnpj);
        if (!CnpjDigitsRegex().IsMatch(normalized))
            throw new ArgumentException("CNPJ must be 14 digits.", nameof(cnpj));

        var url = new Uri($"cnpj/v1/{normalized}", UriKind.Relative);

        LogFetchingByCnpj(normalized);

        var response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            LogCnpjNotFound(normalized);
            return null;
        }

        response.EnsureSuccessStatusCode();

        return await response.Content
            .ReadFromJsonAsync<BrazilCnpjResponse>(JsonOptions, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Strips formatting characters (dots, slash, dash) from a CNPJ string,
    /// leaving only the 14-digit representation.
    /// </summary>
    /// <example>
    /// "33.000.167/0001-01" → "33000167000101"
    /// </example>
    internal static string NormalizeCnpj(string cnpj) =>
        CnpjFormatCharsRegex().Replace(cnpj.Trim(), string.Empty);

    /// <summary>Matches exactly 14 digits (normalized CNPJ).</summary>
    [GeneratedRegex(@"^\d{14}$")]
    private static partial Regex CnpjDigitsRegex();

    /// <summary>Matches formatting characters in a CNPJ (dots, slash, dash, spaces).</summary>
    [GeneratedRegex(@"[\.\-/\s]")]
    private static partial Regex CnpjFormatCharsRegex();

    // ── Logging ──────────────────────────────────────────────────────────────

    [LoggerMessage(Level = LogLevel.Debug, Message = "BrazilCNPJ: Fetching by CNPJ {Cnpj}")]
    private partial void LogFetchingByCnpj(string cnpj);

    [LoggerMessage(Level = LogLevel.Debug, Message = "BrazilCNPJ: CNPJ {Cnpj} not found")]
    private partial void LogCnpjNotFound(string cnpj);
}
