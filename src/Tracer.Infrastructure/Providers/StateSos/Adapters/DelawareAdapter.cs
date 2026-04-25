using AngleSharp;
using AngleSharp.Dom;

namespace Tracer.Infrastructure.Providers.StateSos.Adapters;

/// <summary>
/// Adapter for Delaware Division of Corporations entity search
/// (<c>icis.corp.delaware.gov/ecorp</c>).
/// </summary>
internal sealed class DelawareAdapter : IStateSosAdapter
{
    public string StateCode => "DE";
    public string BaseUrl => "https://icis.corp.delaware.gov/ecorp/";
    public string SearchPath => "entitysearch/namesearch.aspx";

    public Dictionary<string, string> BuildSearchForm(string companyName) =>
        new()
        {
            ["ctl00$ContentPlaceHolder1$frmEntityName"] = companyName,
            ["ctl00$ContentPlaceHolder1$frmFileNumber"] = string.Empty,
        };

    public List<StateSosSearchResult>? ParseResults(string html)
    {
        var config = Configuration.Default;
        var context = BrowsingContext.New(config);
#pragma warning disable CA1849 // AngleSharp Content() parsing is synchronous — no I/O
        var document = context.OpenAsync(req => req.Content(html)).GetAwaiter().GetResult();
#pragma warning restore CA1849

        // Delaware returns results in a grid/table format
        var rows = document.QuerySelectorAll("table tr, .grid-row, [id*='GridView'] tr");
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

            if (string.IsNullOrWhiteSpace(entityName) || string.IsNullOrWhiteSpace(filingNumber))
                continue;

            results.Add(new StateSosSearchResult
            {
                EntityName = entityName,
                FilingNumber = filingNumber,
                StateCode = StateCode,
                Status = status,
                EntityType = entityType,
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
