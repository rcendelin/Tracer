using System.Text.Json;
using Tracer.Domain.Common;
using Tracer.Domain.Enums;
using Tracer.Domain.Events;
using Tracer.Domain.ValueObjects;

namespace Tracer.Domain.Entities;

/// <summary>
/// The golden record in the Company Knowledge Base (CKB).
/// One profile represents one unique company, identified by
/// <see cref="NormalizedKey"/> (RegistrationId + Country or normalised name hash).
/// </summary>
public sealed class CompanyProfile : BaseEntity, IAggregateRoot
{
    // EF Core parameterless constructor
    private CompanyProfile() { }

    /// <summary>
    /// Creates a new company profile in the CKB.
    /// </summary>
    /// <param name="normalizedKey">
    /// The unique lookup key for this profile, e.g. <c>"CZ:12345678"</c> or a normalised name hash.
    /// </param>
    /// <param name="country">ISO 3166-1 alpha-2 country code.</param>
    /// <param name="registrationId">Business registration ID, if known at creation time.</param>
    public CompanyProfile(string normalizedKey, string country, string? registrationId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedKey, nameof(normalizedKey));
        ArgumentException.ThrowIfNullOrWhiteSpace(country, nameof(country));

        NormalizedKey = normalizedKey;
        Country = country;
        RegistrationId = registrationId;
        CreatedAt = DateTimeOffset.UtcNow;
        TraceCount = 0;
        IsArchived = false;

        AddDomainEvent(new ProfileCreatedEvent(Id, normalizedKey));
    }

    // ── Identity ────────────────────────────────────────────────────

    /// <summary>
    /// Gets the unique normalised lookup key for this profile.
    /// Format: <c>"{Country}:{RegistrationId}"</c> or a normalised name hash.
    /// </summary>
    public string NormalizedKey { get; private set; } = null!;

    /// <summary>
    /// Gets the ISO 3166-1 alpha-2 country code.
    /// </summary>
    public string Country { get; private set; } = null!;

    /// <summary>
    /// Gets the business registration ID (e.g. IČO in CZ, CRN in UK).
    /// Stored as a plain string for cross-country compatibility.
    /// </summary>
    public string? RegistrationId { get; private set; }

    // ── Enriched fields (TracedField<T>) ────────────────────────────

    public TracedField<string>? LegalName { get; private set; }
    public TracedField<string>? TradeName { get; private set; }
    public TracedField<string>? TaxId { get; private set; }
    public TracedField<string>? LegalForm { get; private set; }
    public TracedField<Address>? RegisteredAddress { get; private set; }
    public TracedField<Address>? OperatingAddress { get; private set; }
    public TracedField<string>? Phone { get; private set; }
    public TracedField<string>? Email { get; private set; }
    public TracedField<string>? Website { get; private set; }
    public TracedField<string>? Industry { get; private set; }
    public TracedField<string>? EmployeeRange { get; private set; }
    public TracedField<string>? EntityStatus { get; private set; }
    public TracedField<string>? ParentCompany { get; private set; }
    public TracedField<GeoCoordinate>? Location { get; private set; }

    // ── CKB metadata ────────────────────────────────────────────────

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? LastEnrichedAt { get; private set; }
    public DateTimeOffset? LastValidatedAt { get; private set; }
    public int TraceCount { get; private set; }
    public Confidence? OverallConfidence { get; private set; }
    public bool IsArchived { get; private set; }

    // ── Behaviour ───────────────────────────────────────────────────

    /// <summary>
    /// Updates a traced field on this profile. If the value has changed,
    /// raises a <see cref="FieldChangedEvent"/> and optionally a
    /// <see cref="CriticalChangeDetectedEvent"/> for critical severity changes.
    /// </summary>
    /// <typeparam name="T">The type of the field value.</typeparam>
    /// <param name="fieldName">The field identifier.</param>
    /// <param name="newValue">The new traced field value.</param>
    /// <param name="source">The provider or process that produced the new value.</param>
    /// <returns>A <see cref="ChangeEvent"/> if the value changed, or <see langword="null"/> if unchanged.</returns>
    public ChangeEvent? UpdateField<T>(FieldName fieldName, TracedField<T> newValue, string source)
    {
        ArgumentNullException.ThrowIfNull(newValue);
        ArgumentException.ThrowIfNullOrWhiteSpace(source, nameof(source));

        var currentJson = GetFieldValueJson(fieldName);
        var newJson = JsonSerializer.Serialize(newValue.Value);

        if (currentJson == newJson)
            return null;

        SetFieldValue(fieldName, newValue);
        LastEnrichedAt = DateTimeOffset.UtcNow;

        var changeType = currentJson is null ? ChangeType.Created : ChangeType.Updated;
        var severity = ClassifyChangeSeverity(fieldName, changeType);

        var changeEvent = new ChangeEvent(
            Id, fieldName, changeType, severity,
            previousValueJson: currentJson,
            newValueJson: newJson,
            detectedBy: source);

        AddDomainEvent(new FieldChangedEvent(
            Id, changeEvent.Id, fieldName, changeType, severity, currentJson, newJson));

        if (severity == ChangeSeverity.Critical)
            AddDomainEvent(new CriticalChangeDetectedEvent(Id, changeEvent.Id, fieldName, newJson));

        return changeEvent;
    }

    /// <summary>
    /// Checks whether any field on this profile has exceeded its TTL and needs re-validation.
    /// </summary>
    public bool NeedsRevalidation()
    {
        var fieldsToCheck = new (FieldName Name, DateTimeOffset? EnrichedAt)[]
        {
            (FieldName.LegalName, LegalName?.EnrichedAt),
            (FieldName.TradeName, TradeName?.EnrichedAt),
            (FieldName.TaxId, TaxId?.EnrichedAt),
            (FieldName.LegalForm, LegalForm?.EnrichedAt),
            (FieldName.RegisteredAddress, RegisteredAddress?.EnrichedAt),
            (FieldName.OperatingAddress, OperatingAddress?.EnrichedAt),
            (FieldName.Phone, Phone?.EnrichedAt),
            (FieldName.Email, Email?.EnrichedAt),
            (FieldName.Website, Website?.EnrichedAt),
            (FieldName.Industry, Industry?.EnrichedAt),
            (FieldName.EmployeeRange, EmployeeRange?.EnrichedAt),
            (FieldName.EntityStatus, EntityStatus?.EnrichedAt),
            (FieldName.ParentCompany, ParentCompany?.EnrichedAt),
            (FieldName.Location, Location?.EnrichedAt),
        };

        var now = DateTimeOffset.UtcNow;

        foreach (var (name, enrichedAt) in fieldsToCheck)
        {
            if (enrichedAt is null)
                continue;

            var ttl = FieldTtl.For(name).Ttl;
            if (now - enrichedAt.Value > ttl)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Increments the trace count, tracking how many times this profile has been requested.
    /// Used for priority scoring in the re-validation queue.
    /// </summary>
    public void IncrementTraceCount()
    {
        TraceCount++;
    }

    /// <summary>
    /// Updates the overall confidence score for this profile.
    /// </summary>
    public void SetOverallConfidence(Confidence confidence)
    {
        OverallConfidence = confidence;
    }

    /// <summary>
    /// Marks this profile as validated at the current time.
    /// </summary>
    public void MarkValidated()
    {
        LastValidatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Archives this profile, removing it from active queries.
    /// </summary>
    public void Archive()
    {
        IsArchived = true;
    }

    /// <summary>
    /// Restores an archived profile to active status.
    /// </summary>
    public void Unarchive()
    {
        IsArchived = false;
    }

    // ── Private helpers ─────────────────────────────────────────────

    private string? GetFieldValueJson(FieldName fieldName) => fieldName switch
    {
        FieldName.LegalName => LegalName is not null ? JsonSerializer.Serialize(LegalName.Value) : null,
        FieldName.TradeName => TradeName is not null ? JsonSerializer.Serialize(TradeName.Value) : null,
        FieldName.TaxId => TaxId is not null ? JsonSerializer.Serialize(TaxId.Value) : null,
        FieldName.LegalForm => LegalForm is not null ? JsonSerializer.Serialize(LegalForm.Value) : null,
        FieldName.RegisteredAddress => RegisteredAddress is not null ? JsonSerializer.Serialize(RegisteredAddress.Value) : null,
        FieldName.OperatingAddress => OperatingAddress is not null ? JsonSerializer.Serialize(OperatingAddress.Value) : null,
        FieldName.Phone => Phone is not null ? JsonSerializer.Serialize(Phone.Value) : null,
        FieldName.Email => Email is not null ? JsonSerializer.Serialize(Email.Value) : null,
        FieldName.Website => Website is not null ? JsonSerializer.Serialize(Website.Value) : null,
        FieldName.Industry => Industry is not null ? JsonSerializer.Serialize(Industry.Value) : null,
        FieldName.EmployeeRange => EmployeeRange is not null ? JsonSerializer.Serialize(EmployeeRange.Value) : null,
        FieldName.EntityStatus => EntityStatus is not null ? JsonSerializer.Serialize(EntityStatus.Value) : null,
        FieldName.ParentCompany => ParentCompany is not null ? JsonSerializer.Serialize(ParentCompany.Value) : null,
        FieldName.Location => Location is not null ? JsonSerializer.Serialize(Location.Value) : null,
        _ => null,
    };

    private void SetFieldValue<T>(FieldName fieldName, TracedField<T> value)
    {
        switch (fieldName)
        {
            case FieldName.LegalName:
                LegalName = CastField<T, string>(value);
                break;
            case FieldName.TradeName:
                TradeName = CastField<T, string>(value);
                break;
            case FieldName.TaxId:
                TaxId = CastField<T, string>(value);
                break;
            case FieldName.LegalForm:
                LegalForm = CastField<T, string>(value);
                break;
            case FieldName.RegisteredAddress:
                RegisteredAddress = CastField<T, Address>(value);
                break;
            case FieldName.OperatingAddress:
                OperatingAddress = CastField<T, Address>(value);
                break;
            case FieldName.Phone:
                Phone = CastField<T, string>(value);
                break;
            case FieldName.Email:
                Email = CastField<T, string>(value);
                break;
            case FieldName.Website:
                Website = CastField<T, string>(value);
                break;
            case FieldName.Industry:
                Industry = CastField<T, string>(value);
                break;
            case FieldName.EmployeeRange:
                EmployeeRange = CastField<T, string>(value);
                break;
            case FieldName.EntityStatus:
                EntityStatus = CastField<T, string>(value);
                break;
            case FieldName.ParentCompany:
                ParentCompany = CastField<T, string>(value);
                break;
            case FieldName.Location:
                Location = CastField<T, GeoCoordinate>(value);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(fieldName), fieldName,
                    $"Unsupported field: {fieldName}.");
        }
    }

    private static TracedField<TTarget> CastField<TSource, TTarget>(TracedField<TSource> field)
    {
        if (field is TracedField<TTarget> typed)
            return typed;

        throw new InvalidOperationException(
            $"Field type mismatch: expected TracedField<{typeof(TTarget).Name}> but got TracedField<{typeof(TSource).Name}>.");
    }

    private static ChangeSeverity ClassifyChangeSeverity(FieldName fieldName, ChangeType changeType) =>
        fieldName switch
        {
            FieldName.EntityStatus => ChangeSeverity.Critical,
            FieldName.LegalName => ChangeSeverity.Major,
            FieldName.RegisteredAddress => ChangeSeverity.Major,
            // Officers is GDPR-gated and will be handled separately in a future block.
            FieldName.Phone => ChangeSeverity.Minor,
            FieldName.Email => ChangeSeverity.Minor,
            FieldName.Website => ChangeSeverity.Minor,
            FieldName.OperatingAddress => ChangeSeverity.Minor,
            _ => changeType == ChangeType.Created ? ChangeSeverity.Cosmetic : ChangeSeverity.Minor,
        };

    // Officers field is not stored as a TracedField property on CompanyProfile
    // because it is GDPR-gated. It will be handled separately in a future block.
}
