namespace Tracer.Contracts.Models;

/// <summary>
/// Physical address of a company.
/// </summary>
/// <remarks>
/// Fields may be <see langword="null"/> for partially-enriched addresses
/// (e.g. a GLEIF record that provides only city and country, or a registry
/// that stores address as a single unstructured string in <see cref="FormattedAddress"/>).
/// Always check for <see langword="null"/> before using individual fields.
/// </remarks>
public sealed record AddressContract
{
    /// <summary>Street name and number (e.g. "Václavské náměstí 1"). May be null for unstructured addresses.</summary>
    public string? Street { get; init; }

    /// <summary>City or municipality. May be null when only <see cref="FormattedAddress"/> is available.</summary>
    public string? City { get; init; }

    /// <summary>Postal / ZIP code. May be null for some international registries.</summary>
    public string? PostalCode { get; init; }

    /// <summary>Region, state, or county (optional).</summary>
    public string? Region { get; init; }

    /// <summary>ISO 3166-1 alpha-2 country code (e.g. "CZ", "GB", "US").</summary>
    public string? Country { get; init; }

    /// <summary>
    /// Human-readable full address formatted according to local conventions.
    /// Always populated when the address was resolved via Google Maps Places.
    /// Suitable for direct display in FieldForce UI even when structured fields are partial.
    /// </summary>
    public string? FormattedAddress { get; init; }
}
