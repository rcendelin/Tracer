namespace Tracer.Application.DTOs;

/// <summary>
/// DTO containing all enriched fields for a company, with provenance.
/// </summary>
public sealed record EnrichedCompanyDto
{
    public TracedFieldDto<string>? LegalName { get; init; }
    public TracedFieldDto<string>? TradeName { get; init; }
    public TracedFieldDto<string>? TaxId { get; init; }
    public TracedFieldDto<string>? LegalForm { get; init; }
    public TracedFieldDto<AddressDto>? RegisteredAddress { get; init; }
    public TracedFieldDto<AddressDto>? OperatingAddress { get; init; }
    public TracedFieldDto<string>? Phone { get; init; }
    public TracedFieldDto<string>? Email { get; init; }
    public TracedFieldDto<string>? Website { get; init; }
    public TracedFieldDto<string>? Industry { get; init; }
    public TracedFieldDto<string>? EmployeeRange { get; init; }
    public TracedFieldDto<string>? EntityStatus { get; init; }
    public TracedFieldDto<string>? ParentCompany { get; init; }
    public TracedFieldDto<GeoCoordinateDto>? Location { get; init; }
}
