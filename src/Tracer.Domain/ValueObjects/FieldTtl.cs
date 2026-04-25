using Tracer.Domain.Enums;

namespace Tracer.Domain.ValueObjects;

/// <summary>
/// Associates a <see cref="FieldName"/> with its time-to-live policy.
/// Use <see cref="For"/> to obtain the default TTL for a given field,
/// or <see cref="Custom"/> when an override is required (e.g. in tests or tenant config).
/// </summary>
public sealed record FieldTtl
{
    /// <summary>Gets the field this TTL applies to.</summary>
    public FieldName Field { get; init; }

    /// <summary>Gets the time-to-live duration for this field.</summary>
    public TimeSpan Ttl { get; init; }

    private FieldTtl(FieldName field, TimeSpan ttl)
    {
        Field = field;
        Ttl = ttl;
    }

    /// <summary>
    /// Returns the default <see cref="FieldTtl"/> for the given <paramref name="field"/>
    /// based on the platform TTL policy defined in the architecture specification.
    /// </summary>
    /// <remarks>
    /// Default TTLs:
    /// <list type="bullet">
    ///   <item><see cref="FieldName.EntityStatus"/> — 30 days</item>
    ///   <item><see cref="FieldName.Officers"/> — 90 days</item>
    ///   <item><see cref="FieldName.Phone"/>, <see cref="FieldName.Email"/>, <see cref="FieldName.Website"/> — 180 days</item>
    ///   <item><see cref="FieldName.RegisteredAddress"/>, <see cref="FieldName.OperatingAddress"/> — 365 days</item>
    ///   <item><see cref="FieldName.RegistrationId"/>, <see cref="FieldName.TaxId"/> — 730 days</item>
    ///   <item>All other fields — 180 days (default)</item>
    /// </list>
    /// </remarks>
    public static FieldTtl For(FieldName field) => field switch
    {
        FieldName.EntityStatus      => new(field, TimeSpan.FromDays(30)),
        FieldName.Officers          => new(field, TimeSpan.FromDays(90)),
        FieldName.Phone             => new(field, TimeSpan.FromDays(180)),
        FieldName.Email             => new(field, TimeSpan.FromDays(180)),
        FieldName.Website           => new(field, TimeSpan.FromDays(180)),
        FieldName.RegisteredAddress => new(field, TimeSpan.FromDays(365)),
        FieldName.OperatingAddress  => new(field, TimeSpan.FromDays(365)),
        FieldName.RegistrationId    => new(field, TimeSpan.FromDays(730)),
        FieldName.TaxId             => new(field, TimeSpan.FromDays(730)),
        _                           => new(field, TimeSpan.FromDays(180)),
    };

    /// <summary>
    /// Creates a <see cref="FieldTtl"/> with an explicit TTL override.
    /// Intended for tenant-level configuration and testing scenarios.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="ttl"/> is zero or negative.
    /// </exception>
    public static FieldTtl Custom(FieldName field, TimeSpan ttl)
    {
        if (ttl <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(ttl), ttl,
                "TTL must be a positive duration.");

        return new(field, ttl);
    }
}
