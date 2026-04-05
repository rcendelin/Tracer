using System.Globalization;

namespace Tracer.Domain.ValueObjects;

/// <summary>
/// Represents a confidence score in the range [0.0, 1.0].
/// Wraps a <see langword="double"/> with range validation and semantic conversions.
/// </summary>
/// <remarks>
/// Use <see cref="Create"/> or the explicit cast operator for construction.
/// Implicit conversion to <see langword="double"/> allows use in arithmetic expressions
/// without boilerplate unwrapping.
/// </remarks>
public readonly record struct Confidence
{
    /// <summary>Gets the underlying confidence value in [0.0, 1.0].</summary>
    public double Value { get; }

    private Confidence(double value)
    {
        Value = value;
    }

    /// <summary>
    /// Creates a <see cref="Confidence"/> from a <see langword="double"/> value.
    /// </summary>
    /// <param name="value">Must be in [0.0, 1.0].</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when value is outside [0.0, 1.0].</exception>
    public static Confidence Create(double value)
    {
        if (!double.IsFinite(value) || value is < 0.0 or > 1.0)
            throw new ArgumentOutOfRangeException(nameof(value), value,
                "Confidence must be a finite number between 0.0 and 1.0.");

        return new Confidence(value);
    }

    /// <summary>
    /// Implicitly converts a <see cref="Confidence"/> to its underlying <see langword="double"/>.
    /// Named alternative: <see cref="ToDouble"/>.
    /// </summary>
    public static implicit operator double(Confidence c) => c.Value;

    /// <summary>
    /// Explicitly converts a <see langword="double"/> to a <see cref="Confidence"/>.
    /// Throws if the value is outside [0.0, 1.0].
    /// Named alternative: <see cref="FromDouble"/>.
    /// </summary>
    public static explicit operator Confidence(double d) => Create(d);

    /// <summary>Returns the underlying <see langword="double"/> value. Named alternative to the implicit operator.</summary>
    public double ToDouble() => Value;

    /// <summary>Creates a <see cref="Confidence"/> from a <see langword="double"/>. Named alternative to the explicit operator.</summary>
    public static Confidence FromDouble(double value) => Create(value);

    /// <summary>Represents the lowest possible confidence (0.0).</summary>
    public static readonly Confidence Zero = new(0.0);

    /// <summary>Represents the highest possible confidence (1.0).</summary>
    public static readonly Confidence Full = new(1.0);

    /// <inheritdoc/>
    public override string ToString() =>
        Value.ToString("F2", CultureInfo.InvariantCulture);
}
