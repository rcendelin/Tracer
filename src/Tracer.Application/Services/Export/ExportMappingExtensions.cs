using Tracer.Domain.Entities;
using Tracer.Domain.ValueObjects;

namespace Tracer.Application.Services.Export;

/// <summary>
/// Maps domain entities to flat export row records. All strings are passed through
/// <see cref="CsvInjectionSanitizer"/> so both CSV and XLSX outputs are safe against
/// formula injection.
/// </summary>
internal static class ExportMappingExtensions
{
    public static ProfileExportRow ToExportRow(this CompanyProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return new ProfileExportRow
        {
            Id = profile.Id,
            NormalizedKey = CsvInjectionSanitizer.Sanitize(profile.NormalizedKey) ?? string.Empty,
            Country = CsvInjectionSanitizer.Sanitize(profile.Country) ?? string.Empty,
            RegistrationId = CsvInjectionSanitizer.Sanitize(profile.RegistrationId),
            LegalName = CsvInjectionSanitizer.Sanitize(profile.LegalName?.Value),
            TradeName = CsvInjectionSanitizer.Sanitize(profile.TradeName?.Value),
            TaxId = CsvInjectionSanitizer.Sanitize(profile.TaxId?.Value),
            LegalForm = CsvInjectionSanitizer.Sanitize(profile.LegalForm?.Value),
            RegisteredAddress = CsvInjectionSanitizer.Sanitize(FormatAddress(profile.RegisteredAddress?.Value)),
            OperatingAddress = CsvInjectionSanitizer.Sanitize(FormatAddress(profile.OperatingAddress?.Value)),
            Phone = CsvInjectionSanitizer.Sanitize(profile.Phone?.Value),
            Email = CsvInjectionSanitizer.Sanitize(profile.Email?.Value),
            Website = CsvInjectionSanitizer.Sanitize(profile.Website?.Value),
            Industry = CsvInjectionSanitizer.Sanitize(profile.Industry?.Value),
            EmployeeRange = CsvInjectionSanitizer.Sanitize(profile.EmployeeRange?.Value),
            EntityStatus = CsvInjectionSanitizer.Sanitize(profile.EntityStatus?.Value),
            ParentCompany = CsvInjectionSanitizer.Sanitize(profile.ParentCompany?.Value),
            OverallConfidence = profile.OverallConfidence?.Value,
            TraceCount = profile.TraceCount,
            CreatedAt = profile.CreatedAt,
            LastEnrichedAt = profile.LastEnrichedAt,
            LastValidatedAt = profile.LastValidatedAt,
            IsArchived = profile.IsArchived,
        };
    }

    public static ChangeExportRow ToExportRow(this ChangeEvent changeEvent)
    {
        ArgumentNullException.ThrowIfNull(changeEvent);
        return new ChangeExportRow
        {
            Id = changeEvent.Id,
            CompanyProfileId = changeEvent.CompanyProfileId,
            Field = changeEvent.Field,
            ChangeType = changeEvent.ChangeType,
            Severity = changeEvent.Severity,
            // JSON blobs are surfaced verbatim; sanitiser still prefixes a stray
            // leading '=' so a crafted field value does not run as a formula.
            PreviousValueJson = CsvInjectionSanitizer.Sanitize(changeEvent.PreviousValueJson),
            NewValueJson = CsvInjectionSanitizer.Sanitize(changeEvent.NewValueJson),
            DetectedBy = CsvInjectionSanitizer.Sanitize(changeEvent.DetectedBy) ?? string.Empty,
            DetectedAt = changeEvent.DetectedAt,
            IsNotified = changeEvent.IsNotified,
        };
    }

    private static string? FormatAddress(Address? address)
    {
        if (address is null)
            return null;

        if (!string.IsNullOrWhiteSpace(address.FormattedAddress))
            return address.FormattedAddress;

        // "Street, City PostalCode, Region, Country"
        var parts = new List<string>(4);
        if (!string.IsNullOrWhiteSpace(address.Street))
            parts.Add(address.Street);

        var cityLine = string.Join(
            ' ',
            new[] { address.City, address.PostalCode }.Where(s => !string.IsNullOrWhiteSpace(s)));
        if (!string.IsNullOrEmpty(cityLine))
            parts.Add(cityLine);

        if (!string.IsNullOrWhiteSpace(address.Region))
            parts.Add(address.Region);
        if (!string.IsNullOrWhiteSpace(address.Country))
            parts.Add(address.Country);

        return parts.Count > 0 ? string.Join(", ", parts) : null;
    }
}
