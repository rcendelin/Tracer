namespace Tracer.Infrastructure.Providers.AiExtractor;

/// <summary>
/// Structured company information extracted by the AI extractor from unstructured text.
/// All fields are optional — the model fills in what it can determine with confidence.
/// </summary>
internal sealed record AiExtractedData
{
    /// <summary>Gets the legal company name.</summary>
    public string? LegalName { get; init; }

    /// <summary>Gets the primary phone number in E.164 or local format.</summary>
    public string? Phone { get; init; }

    /// <summary>Gets the primary email address.</summary>
    public string? Email { get; init; }

    /// <summary>Gets the company's physical address.</summary>
    public AiExtractedAddress? Address { get; init; }

    /// <summary>Gets the industry or sector (e.g. "Manufacturing", "Retail").</summary>
    public string? Industry { get; init; }

    /// <summary>
    /// Gets the employee count range as a string (e.g. "1-10", "50-200", "1000+").
    /// </summary>
    public string? EmployeeRange { get; init; }

    /// <summary>Gets a brief description of the company's business.</summary>
    public string? Description { get; init; }
}

/// <summary>
/// Postal address extracted by the AI model.
/// </summary>
internal sealed record AiExtractedAddress
{
    public string? Street { get; init; }
    public string? City { get; init; }
    public string? PostalCode { get; init; }
    public string? Country { get; init; }
    public string? Region { get; init; }
}
