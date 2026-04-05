namespace Tracer.Domain.ValueObjects;

/// <summary>
/// Represents a physical address for a company (registered or operating).
/// All required fields must be non-null; optional fields may be absent
/// depending on the data source.
/// </summary>
public sealed record Address
{
    /// <summary>Gets the street name and number.</summary>
    public required string Street { get; init; }

    /// <summary>Gets the city or municipality.</summary>
    public required string City { get; init; }

    /// <summary>Gets the postal / ZIP code.</summary>
    public required string PostalCode { get; init; }

    /// <summary>Gets the region, state, or county (optional).</summary>
    public string? Region { get; init; }

    /// <summary>
    /// Gets the country code in ISO 3166-1 alpha-2 format, e.g. <c>"CZ"</c>, <c>"GB"</c>.
    /// </summary>
    public required string Country { get; init; }

    /// <summary>
    /// Gets the free-text formatted address as returned by the provider.
    /// May include additional context not captured in the structured fields.
    /// </summary>
    public string? FormattedAddress { get; init; }

    /// <summary>
    /// Gets a sentinel empty address instance for EF Core owned-type materialisation.
    /// Do not use in application code to represent "no address" — use a nullable
    /// <c>TracedField&lt;Address&gt;?</c> for that purpose.
    /// </summary>
    public static Address Empty { get; } = new()
    {
        Street = string.Empty,
        City = string.Empty,
        PostalCode = string.Empty,
        Country = string.Empty,
    };
}
