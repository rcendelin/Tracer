using System.Globalization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;

namespace Tracer.Api.OpenApi;

/// <summary>
/// Fills the <see cref="OpenApiDocument"/>'s <c>info</c>, <c>servers</c>,
/// <c>tags</c> and <c>components.securitySchemes</c> sections from
/// <see cref="TracerOpenApiOptions"/> (B-82).
/// </summary>
/// <remarks>
/// Runs once per request for the OpenAPI endpoint. The options snapshot is
/// captured at construction time via <see cref="IOptions{T}"/> so a single
/// transformer instance reflects whatever configuration was valid at app start
/// (consistent with <c>ValidateOnStart</c>).
/// </remarks>
internal sealed class TracerOpenApiDocumentTransformer(
    IOptions<TracerOpenApiOptions> options) : IOpenApiDocumentTransformer
{
    /// <summary>Security scheme key referenced by operation-level requirements.</summary>
    internal const string ApiKeySchemeName = "ApiKey";

    /// <summary>Header name expected by <c>ApiKeyAuthMiddleware</c>.</summary>
    internal const string ApiKeyHeaderName = "X-Api-Key";

    private static readonly Dictionary<string, string> TagDescriptions = new(StringComparer.Ordinal)
    {
        ["Trace"] = "Submit enrichment requests and inspect their results.",
        ["Profiles"] = "Browse the Company Knowledge Base (CKB) golden records, their change history and trigger manual re-validation.",
        ["Changes"] = "Audit trail of field-level changes detected during enrichment.",
        ["Stats"] = "Aggregate dashboard counters (profiles, changes, traces).",
    };

    private readonly TracerOpenApiOptions _options = options.Value;

    public Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);

        document.Info = BuildInfo(_options);
        document.Servers = BuildServers(_options);
        document.Tags = BuildTags();

        // `OpenApiComponents` initializes `SecuritySchemes` in its constructor across the
        // Microsoft.OpenApi 1.x and 2.x preview surfaces the ASP.NET Core preview ships against,
        // so we rely on that instead of pinning a concrete dictionary type (which differs
        // between the two versions).
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes[ApiKeySchemeName] = BuildApiKeyScheme();

        return Task.CompletedTask;
    }

    private static OpenApiInfo BuildInfo(TracerOpenApiOptions options)
    {
        var info = new OpenApiInfo
        {
            Title = options.Title,
            Version = options.Version,
            Description = options.Description,
        };

        if (!string.IsNullOrWhiteSpace(options.TermsOfService)
            && Uri.TryCreate(options.TermsOfService, UriKind.Absolute, out var tos))
        {
            info.TermsOfService = tos;
        }

        if (!string.IsNullOrWhiteSpace(options.ContactName)
            || !string.IsNullOrWhiteSpace(options.ContactEmail))
        {
            info.Contact = new OpenApiContact
            {
                Name = string.IsNullOrWhiteSpace(options.ContactName) ? null : options.ContactName,
                Email = string.IsNullOrWhiteSpace(options.ContactEmail) ? null : options.ContactEmail,
            };
        }

        if (!string.IsNullOrWhiteSpace(options.LicenseName))
        {
            var license = new OpenApiLicense
            {
                Name = options.LicenseName,
            };
            if (!string.IsNullOrWhiteSpace(options.LicenseUrl)
                && Uri.TryCreate(options.LicenseUrl, UriKind.Absolute, out var licenseUri))
            {
                license.Url = licenseUri;
            }
            info.License = license;
        }

        return info;
    }

    private static List<OpenApiServer> BuildServers(TracerOpenApiOptions options)
    {
        var servers = new List<OpenApiServer>(options.ServerUrls.Count);
        foreach (var url in options.ServerUrls)
        {
            if (string.IsNullOrWhiteSpace(url))
                continue;

            if (!Uri.TryCreate(url, UriKind.Absolute, out _))
                continue;

            servers.Add(new OpenApiServer { Url = url });
        }
        return servers;
    }

    private static HashSet<OpenApiTag> BuildTags()
    {
        var tags = new HashSet<OpenApiTag>();
        foreach (var (name, description) in TagDescriptions)
        {
            tags.Add(new OpenApiTag
            {
                Name = name,
                Description = description,
            });
        }
        return tags;
    }

    private static OpenApiSecurityScheme BuildApiKeyScheme()
    {
        return new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.ApiKey,
            In = ParameterLocation.Header,
            Name = ApiKeyHeaderName,
            Description = string.Create(
                CultureInfo.InvariantCulture,
                $"Static API key sent via the `{ApiKeyHeaderName}` header. " +
                "Dev hosts with no configured keys pass all requests through; " +
                "production requires a matching key in `Auth:ApiKeys`. " +
                "SignalR negotiation accepts the same key via `Authorization: Bearer <key>` or `access_token` query."),
        };
    }
}
