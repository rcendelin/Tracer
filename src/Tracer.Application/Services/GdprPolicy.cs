using Microsoft.Extensions.Options;
using Tracer.Domain.Enums;

namespace Tracer.Application.Services;

/// <summary>
/// Default <see cref="IGdprPolicy"/>. The classification map is hard-coded
/// because it follows the platform data model — adding a new personal-data
/// field is an architectural decision, not a runtime toggle, so a code change
/// is required.
/// </summary>
/// <remarks>
/// Current personal-data fields: <see cref="FieldName.Officers"/>. All other
/// <see cref="FieldName"/> members are firmographic.
/// <para>
/// The values returned here MUST match the conceptual classification used by
/// all downstream consumers (opt-in gate in B-70, retention job, audit hook).
/// Changing the classification of an existing field is a breaking change for
/// the retention job and SHOULD be accompanied by a data migration plan.
/// </para>
/// </remarks>
internal sealed class GdprPolicy : IGdprPolicy
{
    private readonly GdprOptions _options;

    public GdprPolicy(IOptions<GdprOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var value = options.Value;
        if (value.PersonalDataRetentionDays <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(options),
                value.PersonalDataRetentionDays,
                "Gdpr:PersonalDataRetentionDays must be a positive number of days.");

        _options = value;
    }

    public TimeSpan PersonalDataRetention => TimeSpan.FromDays(_options.PersonalDataRetentionDays);

    public FieldClassification Classify(FieldName field) => field switch
    {
        FieldName.Officers => FieldClassification.PersonalData,
        _                  => FieldClassification.Firmographic,
    };

    public bool IsPersonalData(FieldName field) => Classify(field) == FieldClassification.PersonalData;

    public bool RequiresConsent(FieldName field) => IsPersonalData(field);
}
