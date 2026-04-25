using Tracer.Application.DTOs;
using Tracer.Contracts.Enums;
using Tracer.Contracts.Messages;
using Tracer.Contracts.Models;

namespace Tracer.Application.Mapping;

/// <summary>
/// Extension methods for mapping between <see cref="Tracer.Contracts"/> types and internal
/// Application DTOs used in Service Bus messages and SubmitTraceCommand processing.
/// </summary>
public static class ContractMappingExtensions
{
    // ── Enum cast helper ──────────────────────────────────────────────────────

    /// <summary>
    /// Casts a Domain enum value to its Contracts counterpart by integer value.
    /// Throws <see cref="InvalidOperationException"/> if the integer value has no defined
    /// member in <typeparamref name="TTarget"/> — prevents undefined enum values from
    /// silently propagating to external consumers if Domain and Contracts enums drift.
    /// </summary>
    private static TTarget MapEnum<TSource, TTarget>(TSource value)
        where TSource : struct, Enum
        where TTarget : struct, Enum
    {
        var intValue = Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);
        var result = (TTarget)(object)intValue;
        if (!Enum.IsDefined(result))
            throw new InvalidOperationException(
                $"Cannot map {typeof(TSource).Name}.{value} to {typeof(TTarget).Name}: " +
                $"integer value {intValue} is not defined in the target enum.");
        return result;
    }

    // ── TraceRequestMessage → TraceRequestDto ─────────────────────────────────

    /// <summary>
    /// Maps a <see cref="TraceRequestMessage"/> received from the Service Bus queue
    /// to the internal <see cref="TraceRequestDto"/> used by <c>SubmitTraceCommand</c>.
    /// </summary>
    public static TraceRequestDto ToTraceRequestDto(this TraceRequestMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return new TraceRequestDto
        {
            CompanyName = message.CompanyName,
            Phone = message.Phone,
            Email = message.Email,
            Website = message.Website,
            Address = message.Address,
            City = message.City,
            Country = message.Country,
            RegistrationId = message.RegistrationId,
            TaxId = message.TaxId,
            IndustryHint = message.IndustryHint,
            Depth = MapEnum<TraceDepth, Domain.Enums.TraceDepth>(message.Depth),
        };
    }

    // ── ChangeEventDto → ChangeEventContract ─────────────────────────────────

    /// <summary>
    /// Maps an internal <see cref="ChangeEventDto"/> to a <see cref="ChangeEventContract"/>
    /// suitable for inclusion in a <see cref="ChangeEventMessage"/>.
    /// </summary>
    public static ChangeEventContract ToContract(this ChangeEventDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);
        return new ChangeEventContract
        {
            Id = dto.Id,
            Field = MapEnum<Domain.Enums.FieldName, FieldName>(dto.Field),
            ChangeType = MapEnum<Domain.Enums.ChangeType, ChangeType>(dto.ChangeType),
            Severity = MapEnum<Domain.Enums.ChangeSeverity, ChangeSeverity>(dto.Severity),
            PreviousValueJson = dto.PreviousValueJson,
            NewValueJson = dto.NewValueJson,
            DetectedBy = dto.DetectedBy,
            DetectedAt = dto.DetectedAt,
        };
    }

    // ── TraceResultDto → TraceResponseMessage ─────────────────────────────────

    /// <summary>
    /// Builds a <see cref="TraceResponseMessage"/> from an internal <see cref="TraceResultDto"/>,
    /// attaching the caller's <paramref name="correlationId"/> for request-reply correlation.
    /// </summary>
    public static TraceResponseMessage ToResponseMessage(this TraceResultDto dto, string correlationId)
    {
        ArgumentNullException.ThrowIfNull(dto);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);

        return new TraceResponseMessage
        {
            TraceId = dto.TraceId,
            CorrelationId = correlationId,
            Status = MapEnum<Domain.Enums.TraceStatus, TraceStatus>(dto.Status),
            Company = dto.Company?.ToContract(),
            Sources = dto.Sources?.Select(s => s.ToContract()).ToList(),
            OverallConfidence = dto.OverallConfidence,
            CreatedAt = dto.CreatedAt,
            CompletedAt = dto.CompletedAt,
            DurationMs = dto.DurationMs,
            FailureReason = dto.FailureReason,
        };
    }

    // ── EnrichedCompanyDto → EnrichedCompanyContract ──────────────────────────

    private static EnrichedCompanyContract ToContract(this EnrichedCompanyDto dto) =>
        new()
        {
            LegalName = dto.LegalName?.ToStringContract(),
            TradeName = dto.TradeName?.ToStringContract(),
            TaxId = dto.TaxId?.ToStringContract(),
            LegalForm = dto.LegalForm?.ToStringContract(),
            RegisteredAddress = dto.RegisteredAddress?.ToAddressContract(),
            OperatingAddress = dto.OperatingAddress?.ToAddressContract(),
            Phone = dto.Phone?.ToStringContract(),
            Email = dto.Email?.ToStringContract(),
            Website = dto.Website?.ToStringContract(),
            Industry = dto.Industry?.ToStringContract(),
            EmployeeRange = dto.EmployeeRange?.ToStringContract(),
            EntityStatus = dto.EntityStatus?.ToStringContract(),
            ParentCompany = dto.ParentCompany?.ToStringContract(),
            Location = dto.Location?.ToGeoContract(),
        };

    // ── SourceResultDto → SourceResultContract ────────────────────────────────

    private static SourceResultContract ToContract(this SourceResultDto dto) =>
        new()
        {
            ProviderId = dto.ProviderId,
            Status = MapEnum<Domain.Enums.SourceStatus, SourceStatus>(dto.Status),
            FieldsEnriched = dto.FieldsEnriched,
            DurationMs = dto.DurationMs,
            ErrorMessage = dto.ErrorMessage,
        };

    // ── TracedField helpers ───────────────────────────────────────────────────

    private static TracedFieldContract<string> ToStringContract(this TracedFieldDto<string> dto) =>
        new()
        {
            Value = dto.Value,
            Confidence = dto.Confidence,
            Source = dto.Source,
            EnrichedAt = dto.EnrichedAt,
        };

    private static TracedFieldContract<AddressContract> ToAddressContract(this TracedFieldDto<AddressDto> dto) =>
        new()
        {
            Value = new AddressContract
            {
                Street = dto.Value.Street,
                City = dto.Value.City,
                PostalCode = dto.Value.PostalCode,
                Region = dto.Value.Region,
                Country = dto.Value.Country,
                FormattedAddress = dto.Value.FormattedAddress,
            },
            Confidence = dto.Confidence,
            Source = dto.Source,
            EnrichedAt = dto.EnrichedAt,
        };

    private static TracedFieldContract<GeoCoordinateContract> ToGeoContract(this TracedFieldDto<GeoCoordinateDto> dto) =>
        new()
        {
            Value = new GeoCoordinateContract
            {
                Latitude = dto.Value.Latitude,
                Longitude = dto.Value.Longitude,
            },
            Confidence = dto.Confidence,
            Source = dto.Source,
            EnrichedAt = dto.EnrichedAt,
        };
}
