using AngleSharp;
using AngleSharp.Dom;

namespace Tracer.Infrastructure.Providers.StateSos.Adapters;

/// <summary>
/// Adapter for California Secretary of State business search
/// (<c>businesssearch.sos.ca.gov</c>).
/// </summary>
internal sealed class CaliforniaAdapter : IStateSosAdapter
{
    public string StateCode => "CA";
    public string BaseUrl => "https://businesssearch.sos.ca.gov/";
    public string SearchPath => "CBS/SearchResults";

    public Dictionary<string, string> BuildSearchForm(string companyName) =>
        new()
        {
            ["SearchType"] = "CORP",
            ["SearchCriteria"] = companyName,
            ["SearchSubType"] = "Keyword",
        };

    public List<StateSosSearchResult>? ParseResults(string html)
    {
        var config = Configuration.Default;
        var context = BrowsingContext.New(config);
#pragma warning disable CA1849 // AngleSharp Content() parsing is synchronous — no I/O
        var document = context.OpenAsync(req => req.Content(html)).GetAwaiter().GetResult();
#pragma warning restore CA1849

        // California returns results in a table with entity data rows
        var rows = document.QuerySelectorAll("table tbody tr, .search-results tr");
        if (rows.Length == 0)
            return null;

        var results = new List<StateSosSearchResult>();

        foreach (var row in rows)
        {
            var cells = row.QuerySelectorAll("td");
            if (cells.Length < 3)
                continue;

            var entityName = CleanText(cells[0].TextContent);
            var filingNumber = CleanText(cells.Length > 1 ? cells[1].TextContent : null);
            var status = CleanText(cells.Length > 2 ? cells[2].TextContent : null);
            var entityType = CleanText(cells.Length > 3 ? cells[3].TextContent : null);
            var formationDate = CleanText(cells.Length > 4 ? cells[4].TextContent : null);

            if (string.IsNullOrWhiteSpace(entityName) || string.IsNullOrWhiteSpace(filingNumber))
                continue;

            results.Add(new StateSosSearchResult
            {
                EntityName = entityName,
                FilingNumber = filingNumber,
                StateCode = StateCode,
                Status = status,
                EntityType = entityType,
                FormationDate = formationDate,
            });
        }

        return results.Count > 0 ? results : null;
    }

    private static string? CleanText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        return text.Trim();
    }
}
