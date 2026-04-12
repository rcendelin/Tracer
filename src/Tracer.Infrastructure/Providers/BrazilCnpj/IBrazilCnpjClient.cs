namespace Tracer.Infrastructure.Providers.BrazilCnpj;

/// <summary>
/// HTTP client abstraction for the BrasilAPI CNPJ endpoint.
/// Base URL: <c>https://brasilapi.com.br/api/</c>
/// </summary>
internal interface IBrazilCnpjClient
{
    /// <summary>
    /// Gets company data by CNPJ number.
    /// </summary>
    /// <param name="cnpj">
    /// The CNPJ number — accepts both formatted (<c>33.000.167/0001-01</c>)
    /// and raw 14-digit (<c>33000167000101</c>) formats.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The company data, or <see langword="null"/> if not found.</returns>
    Task<BrazilCnpjResponse?> GetByCnpjAsync(string cnpj, CancellationToken cancellationToken);
}
