namespace Tracer.Application.DTOs;

/// <summary>
/// DTO for a physical address.
/// </summary>
public sealed record AddressDto
{
    public required string Street { get; init; }
    public required string City { get; init; }
    public required string PostalCode { get; init; }
    public string? Region { get; init; }
    public required string Country { get; init; }
    public string? FormattedAddress { get; init; }
}
