using Microsoft.Extensions.Logging;
using Tracer.Domain.Entities;
using Tracer.Domain.Enums;
using Tracer.Domain.ValueObjects;

namespace Tracer.Application.Services;

/// <summary>
/// Compares newly enriched fields against the current state of a
/// <see cref="CompanyProfile"/> and produces a list of <see cref="ChangeEvent"/>s
/// with severity classification. Each changed field is applied to the profile
/// in-place (triggering domain events).
/// </summary>
public sealed partial class ChangeDetector : IChangeDetector
{
    private readonly ILogger<ChangeDetector> _logger;

    public ChangeDetector(ILogger<ChangeDetector> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public ChangeDetectionResult DetectChanges(
        CompanyProfile profile,
        IReadOnlyDictionary<FieldName, TracedField<object>> newFields)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(newFields);

        if (newFields.Count == 0)
            return ChangeDetectionResult.Empty;

        var changeEvents = new List<ChangeEvent>();

        foreach (var (fieldName, tracedField) in newFields)
        {
            var changeEvent = ApplyField(profile, fieldName, tracedField);

            if (changeEvent is not null)
                changeEvents.Add(changeEvent);
        }

        var result = new ChangeDetectionResult { Changes = changeEvents };

        if (result.TotalChanges > 0)
            LogChangesDetected(profile.NormalizedKey, result.TotalChanges, result.HasCriticalChanges);

        if (result.HasCriticalChanges)
            LogCriticalChanges(profile.NormalizedKey, result.GetBySeverity(ChangeSeverity.Critical).Count);

        return result;
    }

    private ChangeEvent? ApplyField(
        CompanyProfile profile,
        FieldName fieldName,
        TracedField<object> tracedField)
    {
        return fieldName switch
        {
            FieldName.RegisteredAddress or FieldName.OperatingAddress
                when tracedField.Value is Address addr
                => profile.UpdateField(fieldName, CreateTyped(tracedField, addr), tracedField.Source),

            FieldName.Location when tracedField.Value is GeoCoordinate geo
                => profile.UpdateField(fieldName, CreateTyped(tracedField, geo), tracedField.Source),

            _ when tracedField.Value is string strVal
                => profile.UpdateField(fieldName, CreateTyped(tracedField, strVal), tracedField.Source),

            _ => LogAndSkipUnrecognized(profile.NormalizedKey, fieldName, tracedField.Value?.GetType()),
        };
    }

    private ChangeEvent? LogAndSkipUnrecognized(string normalizedKey, FieldName fieldName, Type? valueType)
    {
        LogUnrecognizedFieldType(normalizedKey, fieldName, valueType?.Name ?? "null");
        return null;
    }

    private static TracedField<T> CreateTyped<T>(TracedField<object> source, T value) =>
        new()
        {
            Value = value,
            Confidence = source.Confidence,
            Source = source.Source,
            EnrichedAt = source.EnrichedAt,
        };

    [LoggerMessage(Level = LogLevel.Information,
        Message = "ChangeDetector: {NormalizedKey} — {ChangeCount} change(s) detected, critical={HasCritical}")]
    private partial void LogChangesDetected(string normalizedKey, int changeCount, bool hasCritical);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "ChangeDetector: {NormalizedKey} — {CriticalCount} CRITICAL change(s) detected")]
    private partial void LogCriticalChanges(string normalizedKey, int criticalCount);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "ChangeDetector: {NormalizedKey} — skipped field {FieldName}, unrecognized value type {ValueType}")]
    private partial void LogUnrecognizedFieldType(string normalizedKey, FieldName fieldName, string valueType);
}
