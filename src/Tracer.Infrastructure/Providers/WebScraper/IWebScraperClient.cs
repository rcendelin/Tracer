namespace Tracer.Infrastructure.Providers.WebScraper;

/// <summary>
/// Fetches a company website and extracts structured contact and identity data.
/// Used by <c>WebScraperProvider</c> (B-55) as the data access layer.
/// </summary>
internal interface IWebScraperClient
{
    /// <summary>
    /// Fetches the page at <paramref name="url"/> and extracts structured company data.
    /// </summary>
    /// <param name="url">The HTTP/HTTPS URL to scrape. Must be an absolute URL.</param>
    /// <param name="ct">Cancellation token; also used to detect caller-side cancellation vs. Polly timeout.</param>
    /// <returns>
    /// A <see cref="WebScrapingResult"/> if structured data was found, or <see langword="null"/> if
    /// the URL is unreachable, returns a non-HTML content type, or yields no extractable data.
    /// </returns>
    Task<WebScrapingResult?> ScrapeAsync(string url, CancellationToken ct);
}
