namespace Tracer.Infrastructure.Providers.StateSos;

/// <summary>
/// Strategy interface for per-state Secretary of State registry adapters.
/// Each state has its own search URL, form parameters, and HTML result structure.
/// The <see cref="StateSosClient"/> dispatches to the appropriate adapter based on
/// <see cref="StateCode"/>.
/// </summary>
internal interface IStateSosAdapter
{
    /// <summary>Gets the two-letter US state code (e.g. "CA", "DE", "NY").</summary>
    string StateCode { get; }

    /// <summary>Gets the base URL of the state's business search portal.</summary>
    string BaseUrl { get; }

    /// <summary>Gets the relative path for the search endpoint.</summary>
    string SearchPath { get; }

    /// <summary>
    /// Builds the form data for a company name search.
    /// </summary>
    /// <param name="companyName">The company name to search for.</param>
    /// <returns>Form data key-value pairs for a POST request.</returns>
    Dictionary<string, string> BuildSearchForm(string companyName);

    /// <summary>
    /// Parses the HTML response from the state's search endpoint into normalized results.
    /// </summary>
    /// <param name="html">The raw HTML response body.</param>
    /// <returns>
    /// A list of matching entities, or <see langword="null"/> if no results were found
    /// or the HTML could not be parsed.
    /// </returns>
    List<StateSosSearchResult>? ParseResults(string html);
}
