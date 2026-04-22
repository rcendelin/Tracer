using System.ComponentModel.DataAnnotations;

namespace Tracer.Api.OpenApi;

/// <summary>
/// Configuration for the public OpenAPI document served by Tracer.Api (B-82).
/// Bound from the "OpenApi" configuration section in <c>appsettings.json</c>.
/// </summary>
/// <remarks>
/// <para>
/// Defaults live in <c>appsettings.json</c> (development surface). Production should
/// override <see cref="ContactEmail"/>, <see cref="ServerUrls"/> and (optionally)
/// <see cref="TermsOfService"/> via Key Vault / App Service configuration so the
/// hosted spec lists the canonical support contact and environment URL.
/// </para>
/// <para>
/// The document itself is built by
/// <see cref="TracerOpenApiDocumentTransformer"/>; the operation-level
/// security requirement (<see cref="ApiKeySecurityRequirementTransformer"/>)
/// reads the same options to stay in sync.
/// </para>
/// </remarks>
public sealed class TracerOpenApiOptions
{
    /// <summary>Configuration section name: <c>"OpenApi"</c>.</summary>
    public const string SectionName = "OpenApi";

    /// <summary>Document title (e.g. "Tracer API"). Required.</summary>
    [Required]
    [StringLength(120, MinimumLength = 1)]
    public string Title { get; set; } = "Tracer API";

    /// <summary>Document version (e.g. "v1"). Required.</summary>
    [Required]
    [StringLength(20, MinimumLength = 1)]
    public string Version { get; set; } = "v1";

    /// <summary>Free-text description shown at the top of the document.</summary>
    public string? Description { get; set; }

    /// <summary>Contact team / group name.</summary>
    public string? ContactName { get; set; }

    /// <summary>Contact e-mail (validated as a well-formed address when set).</summary>
    [EmailAddress]
    public string? ContactEmail { get; set; }

    /// <summary>License name (e.g. "Proprietary", "MIT").</summary>
    public string? LicenseName { get; set; }

    /// <summary>Optional URL pointing to the license text.</summary>
    [Url]
    public string? LicenseUrl { get; set; }

    /// <summary>Optional URL pointing to the terms of service document.</summary>
    [Url]
    public string? TermsOfService { get; set; }

    /// <summary>
    /// Absolute server URLs advertised in the document (<c>servers[]</c>).
    /// When empty, the hosting URL is used implicitly by clients.
    /// </summary>
    public IList<string> ServerUrls { get; } = [];

    /// <summary>
    /// When <c>true</c>, the Scalar UI is mounted at <c>/scalar/{documentName}</c>.
    /// Defaults to <c>true</c> in Development, <c>false</c> in Production unless
    /// explicitly opted in.
    /// </summary>
    public bool EnableUi { get; set; }
}
