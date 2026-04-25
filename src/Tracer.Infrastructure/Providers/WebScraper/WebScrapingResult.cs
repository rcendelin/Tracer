namespace Tracer.Infrastructure.Providers.WebScraper;

/// <summary>
/// Structured data extracted from a company website by the web scraper.
/// All fields are optional — the scraper fills in what it can find.
/// </summary>
internal sealed record WebScrapingResult
{
    /// <summary>Gets the canonical source URL that was scraped.</summary>
    public required string SourceUrl { get; init; }

    /// <summary>Gets the company name extracted from JSON-LD, Open Graph, or page title.</summary>
    public string? CompanyName { get; init; }

    /// <summary>Gets the primary phone number found on the page.</summary>
    public string? Phone { get; init; }

    /// <summary>Gets the primary email address found on the page.</summary>
    public string? Email { get; init; }

    /// <summary>Gets the canonical website URL from JSON-LD or meta tags.</summary>
    public string? Website { get; init; }

    /// <summary>Gets the company description from JSON-LD or meta description.</summary>
    public string? Description { get; init; }

    /// <summary>Gets the industry/sector hint extracted from schema.org type or meta tags.</summary>
    public string? Industry { get; init; }

    /// <summary>Gets the postal address extracted from schema.org PostalAddress or microdata.</summary>
    public ScrapedAddress? Address { get; init; }
}

/// <summary>
/// A postal address extracted from schema.org <c>PostalAddress</c> or equivalent structured data.
/// </summary>
internal sealed record ScrapedAddress
{
    public string? Street { get; init; }
    public string? City { get; init; }
    public string? PostalCode { get; init; }
    public string? Country { get; init; }
    public string? Region { get; init; }
}
