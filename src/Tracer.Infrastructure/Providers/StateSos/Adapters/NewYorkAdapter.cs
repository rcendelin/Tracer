using AngleSharp;
using AngleSharp.Dom;

namespace Tracer.Infrastructure.Providers.StateSos.Adapters;

/// <summary>
/// Adapter for New York Department of State corporation search
/// (<c>appext20.dos.ny.gov/corp_public</c>).
/// </summary>
internal sealed class NewYorkAdapter : IStateSosAdapter
{
    public string StateCode => "NY";
    public string BaseUrl => "https://appext20.dos.ny.gov/corp_public/";
    public string SearchPath => "CORPSEARCH.SELECT_ENTITY";

    public Dictionary<string, string> BuildSearchForm(string companyName) =>
        new()
        {
            ["p_entity_name"] = companyName,
            ["p_name_type"] = "%25", // "Contains" search mode (URL-encoded %)
            ["p_search_type"] = "BEGINS",
        };

    public List<StateSosSearchResult>? ParseResults(string html)
    {
        var config = Configuration.Default;
        var context = BrowsingContext.New(config);
#pragma warning disable CA1849 // AngleSharp Content() parsing is synchronous — no I/O
        var document = context.OpenAsync(req => req.Content(html)).GetAwaiter().GetResult();
#pragma warning restore CA1849

        // New York returns results in a table format
        var rows = document.QuerySelectorAll("table tr, .search-results tr");
        if (rows.Length == 0)
            return null;

        var results = new List<StateSosSearchResult>();

        foreach (var row in rows)
        {
            var cells = row.QuerySelectorAll("td");
            if (cells.Length < 2)
                continue;

            var entityName = CleanText(cells[0].TextContent);
            var filingNumber = CleanText(cells.Length > 1 ? cells[1].TextContent : null);
            var entityType = CleanText(cells.Length > 2 ? cells[2].TextContent : null);
            var status = CleanText(cells.Length > 3 ? cells[3].TextContent : null);
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
