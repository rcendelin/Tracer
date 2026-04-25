using Tracer.Application.DTOs;
using Tracer.Domain.Entities;

namespace Tracer.Application.Mapping;

/// <summary>
/// Extension methods for mapping <see cref="CompanyProfile"/> to DTOs.
/// </summary>
public static class CompanyProfileMappingExtensions
{
    public static CompanyProfileDto ToDto(this CompanyProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return new CompanyProfileDto
        {
            Id = profile.Id,
        NormalizedKey = profile.NormalizedKey,
        Country = profile.Country,
        RegistrationId = profile.RegistrationId,
        Enriched = profile.ToEnrichedDto(),
        CreatedAt = profile.CreatedAt,
        LastEnrichedAt = profile.LastEnrichedAt,
        LastValidatedAt = profile.LastValidatedAt,
        TraceCount = profile.TraceCount,
        OverallConfidence = profile.OverallConfidence?.Value,
            IsArchived = profile.IsArchived,
        };
    }

    public static EnrichedCompanyDto ToEnrichedDto(this CompanyProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return new EnrichedCompanyDto
        {
            LegalName = profile.LegalName.ToDto(),
        TradeName = profile.TradeName.ToDto(),
        TaxId = profile.TaxId.ToDto(),
        LegalForm = profile.LegalForm.ToDto(),
        RegisteredAddress = profile.RegisteredAddress.ToDto(),
        OperatingAddress = profile.OperatingAddress.ToDto(),
        Phone = profile.Phone.ToDto(),
        Email = profile.Email.ToDto(),
        Website = profile.Website.ToDto(),
        Industry = profile.Industry.ToDto(),
        EmployeeRange = profile.EmployeeRange.ToDto(),
        EntityStatus = profile.EntityStatus.ToDto(),
        ParentCompany = profile.ParentCompany.ToDto(),
            Location = profile.Location.ToDto(),
            // B-93: Officers — GDPR strip happens upstream in WaterfallOrchestrator
            // (when TraceRequest.IncludeOfficers = false), so this mapping faithfully
            // surfaces whatever made it into the persisted CKB profile.
            Officers = profile.Officers.ToDto(),
        };
    }
}
