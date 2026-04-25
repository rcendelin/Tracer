using System.Text.Encodings.Web;
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
    // UnsafeRelaxedJsonEscaping keeps +, <, >, &, etc. as-is for human-readable change event JSON.
    // These strings are stored in ChangeEvents, exposed via GET /api/changes, and pushed via SignalR.
    // XSS risk is mitigated at the rendering layer: React JSX auto-escapes all interpolated values.
    // Do NOT render this data via innerHTML or any unsafe HTML injection mechanism (CWE-79).
    private static readonly JsonSerializerOptions ChangeJsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

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

    /// <summary>
    /// Directors / officers (B-93). GDPR-gated <see cref="Tracer.Domain.Enums.FieldClassification.PersonalData"/>;
    /// the <c>WaterfallOrchestrator</c> strips this field upstream when
    /// <c>TraceRequest.IncludeOfficers = false</c> (per `IGdprPolicy.PersonalDataFields`),
    /// so this property is only populated for explicit opt-in traces.
    /// </summary>
    public TracedField<IReadOnlyList<string>>? Officers { get; private set; }

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
        var newJson = JsonSerializer.Serialize(newValue.Value, ChangeJsonOptions);

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
    /// <remarks>
    /// This domain-level baseline uses the platform defaults in
    /// <see cref="FieldTtl.For"/>. Application code that must honour
    /// per-environment TTL overrides (e.g. the re-validation scheduler)
    /// should use <c>IFieldTtlPolicy</c> from the Application layer instead.
    /// </remarks>
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
            // B-93: Officers participates in TTL sweep when it's been enriched.
            // GDPR gating happens at the orchestrator strip boundary; once the
            // value reaches CKB, normal TTL semantics apply.
            (FieldName.Officers, Officers?.EnrichedAt),
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
        FieldName.LegalName => LegalName is not null ? JsonSerializer.Serialize(LegalName.Value, ChangeJsonOptions) : null,
        FieldName.TradeName => TradeName is not null ? JsonSerializer.Serialize(TradeName.Value, ChangeJsonOptions) : null,
        FieldName.TaxId => TaxId is not null ? JsonSerializer.Serialize(TaxId.Value, ChangeJsonOptions) : null,
        FieldName.LegalForm => LegalForm is not null ? JsonSerializer.Serialize(LegalForm.Value, ChangeJsonOptions) : null,
        FieldName.RegisteredAddress => RegisteredAddress is not null ? JsonSerializer.Serialize(RegisteredAddress.Value, ChangeJsonOptions) : null,
        FieldName.OperatingAddress => OperatingAddress is not null ? JsonSerializer.Serialize(OperatingAddress.Value, ChangeJsonOptions) : null,
        FieldName.Phone => Phone is not null ? JsonSerializer.Serialize(Phone.Value, ChangeJsonOptions) : null,
        FieldName.Email => Email is not null ? JsonSerializer.Serialize(Email.Value, ChangeJsonOptions) : null,
        FieldName.Website => Website is not null ? JsonSerializer.Serialize(Website.Value, ChangeJsonOptions) : null,
        FieldName.Industry => Industry is not null ? JsonSerializer.Serialize(Industry.Value, ChangeJsonOptions) : null,
        FieldName.EmployeeRange => EmployeeRange is not null ? JsonSerializer.Serialize(EmployeeRange.Value, ChangeJsonOptions) : null,
        FieldName.EntityStatus => EntityStatus is not null ? JsonSerializer.Serialize(EntityStatus.Value, ChangeJsonOptions) : null,
        FieldName.ParentCompany => ParentCompany is not null ? JsonSerializer.Serialize(ParentCompany.Value, ChangeJsonOptions) : null,
        FieldName.Location => Location is not null ? JsonSerializer.Serialize(Location.Value, ChangeJsonOptions) : null,
        FieldName.Officers => Officers is not null ? JsonSerializer.Serialize(Officers.Value, ChangeJsonOptions) : null,
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
            case FieldName.Officers:
                Officers = CastField<T, IReadOnlyList<string>>(value);
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
            FieldName.Officers => ChangeSeverity.Major, // B-93: GDPR-gated, but officer changes are business-significant
            FieldName.Phone => ChangeSeverity.Minor,
            FieldName.Email => ChangeSeverity.Minor,
            FieldName.Website => ChangeSeverity.Minor,
            FieldName.OperatingAddress => ChangeSeverity.Minor,
            _ => changeType == ChangeType.Created ? ChangeSeverity.Cosmetic : ChangeSeverity.Minor,
        };

    // B-93: Officers IS now a TracedField property — see declaration above.
    // GDPR gating is enforced upstream in the WaterfallOrchestrator (it strips
    // PersonalData fields per `IGdprPolicy.PersonalDataFields` when
    // `TraceRequest.IncludeOfficers = false`), so the property is only set
    // for explicit opt-in traces.

    /// <summary>
    /// Refreshes <see cref="TracedField{T}.EnrichedAt"/> on <paramref name="fieldName"/>
    /// to <paramref name="now"/> without changing the value or source. Used by the
    /// B-66 lightweight re-validation runner to extend the TTL of fields that have
    /// expired but whose value has not actually changed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// **No domain event** is raised — the contract is "the value is still correct
    /// as of <paramref name="now"/>", which is precisely what `EnrichedAt` records.
    /// </para>
    /// <para>
    /// The method is idempotent / silent when the field has no value (null), and
    /// monotonic when <paramref name="now"/> is earlier than the current
    /// <c>EnrichedAt</c> — the existing value wins. Both cases short-circuit
    /// rather than throw because the lightweight runner walks several fields and
    /// must not give up on an inconsistent clock.
    /// </para>
    /// </remarks>
    public void RefreshFieldEnrichedAt(FieldName fieldName, DateTimeOffset now)
    {
        switch (fieldName)
        {
            case FieldName.LegalName:
                if (LegalName is not null && LegalName.EnrichedAt < now)
                    LegalName = LegalName with { EnrichedAt = now };
                break;
            case FieldName.TradeName:
                if (TradeName is not null && TradeName.EnrichedAt < now)
                    TradeName = TradeName with { EnrichedAt = now };
                break;
            case FieldName.TaxId:
                if (TaxId is not null && TaxId.EnrichedAt < now)
                    TaxId = TaxId with { EnrichedAt = now };
                break;
            case FieldName.LegalForm:
                if (LegalForm is not null && LegalForm.EnrichedAt < now)
                    LegalForm = LegalForm with { EnrichedAt = now };
                break;
            case FieldName.RegisteredAddress:
                if (RegisteredAddress is not null && RegisteredAddress.EnrichedAt < now)
                    RegisteredAddress = RegisteredAddress with { EnrichedAt = now };
                break;
            case FieldName.OperatingAddress:
                if (OperatingAddress is not null && OperatingAddress.EnrichedAt < now)
                    OperatingAddress = OperatingAddress with { EnrichedAt = now };
                break;
            case FieldName.Phone:
                if (Phone is not null && Phone.EnrichedAt < now)
                    Phone = Phone with { EnrichedAt = now };
                break;
            case FieldName.Email:
                if (Email is not null && Email.EnrichedAt < now)
                    Email = Email with { EnrichedAt = now };
                break;
            case FieldName.Website:
                if (Website is not null && Website.EnrichedAt < now)
                    Website = Website with { EnrichedAt = now };
                break;
            case FieldName.Industry:
                if (Industry is not null && Industry.EnrichedAt < now)
                    Industry = Industry with { EnrichedAt = now };
                break;
            case FieldName.EmployeeRange:
                if (EmployeeRange is not null && EmployeeRange.EnrichedAt < now)
                    EmployeeRange = EmployeeRange with { EnrichedAt = now };
                break;
            case FieldName.EntityStatus:
                if (EntityStatus is not null && EntityStatus.EnrichedAt < now)
                    EntityStatus = EntityStatus with { EnrichedAt = now };
                break;
            case FieldName.ParentCompany:
                if (ParentCompany is not null && ParentCompany.EnrichedAt < now)
                    ParentCompany = ParentCompany with { EnrichedAt = now };
                break;
            case FieldName.Location:
                if (Location is not null && Location.EnrichedAt < now)
                    Location = Location with { EnrichedAt = now };
                break;
            case FieldName.Officers:
                // B-93: Officers is a TracedField. GDPR gating is upstream.
                if (Officers is not null && Officers.EnrichedAt < now)
                    Officers = Officers with { EnrichedAt = now };
                break;
            // RegistrationId is not a TracedField<T> (no EnrichedAt) and falls
            // through to default — refresh has nothing to do.
            default:
                break;
        }
    }
}
