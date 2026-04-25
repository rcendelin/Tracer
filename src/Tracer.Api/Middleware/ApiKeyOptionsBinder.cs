using System.Globalization;

namespace Tracer.Api.Middleware;

/// <summary>
/// Projects the <c>Auth:ApiKeys</c> configuration section into
/// <see cref="ApiKeyOptions"/>. Accepts either:
/// <list type="bullet">
///   <item>Legacy flat form: <c>Auth:ApiKeys:0 = "the-key"</c></item>
///   <item>Structured form: <c>Auth:ApiKeys:0:Key</c> + <c>:Label</c> + <c>:ExpiresAt</c></item>
/// </list>
/// A mix of both forms is permitted in the same array.
/// </summary>
internal static class ApiKeyOptionsBinder
{
    public static ApiKeyOptions Bind(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var section = configuration.GetSection(ApiKeyOptions.SectionName + ":ApiKeys");
        var entries = new List<ApiKeyEntry>();

        foreach (var child in section.GetChildren())
        {
            // Flat form: child.Value is the key, child has no sub-keys.
            if (!child.GetChildren().Any())
            {
                if (string.IsNullOrWhiteSpace(child.Value))
                    continue;

                entries.Add(new ApiKeyEntry { Key = child.Value });
                continue;
            }

            // Structured form.
            var keyValue = child["Key"];
            if (string.IsNullOrWhiteSpace(keyValue))
                throw new InvalidOperationException(
                    $"Auth:ApiKeys['{child.Key}']:Key is missing. Provide either a plain string value or an object with a Key property.");

            DateTimeOffset? expiresAt = null;
            var expiresAtRaw = child["ExpiresAt"];
            if (!string.IsNullOrWhiteSpace(expiresAtRaw))
            {
                if (!DateTimeOffset.TryParse(
                        expiresAtRaw,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                        out var parsed))
                {
                    throw new InvalidOperationException(
                        $"Auth:ApiKeys['{child.Key}']:ExpiresAt is not a valid ISO 8601 timestamp (got '{expiresAtRaw}').");
                }

                expiresAt = parsed;
            }

            entries.Add(new ApiKeyEntry
            {
                Key = keyValue,
                Label = child["Label"],
                ExpiresAt = expiresAt,
            });
        }

        return new ApiKeyOptions { ApiKeys = entries };
    }

    /// <summary>
    /// Validates the bound options. Returns an error message on failure so
    /// callers can surface it through <c>IValidateOptions</c> without
    /// throwing raw exceptions.
    /// </summary>
    public static string? Validate(ApiKeyOptions options, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(options);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var index = 0;
        foreach (var entry in options.ApiKeys)
        {
            if (string.IsNullOrWhiteSpace(entry.Key))
                return $"Auth:ApiKeys[{index}]:Key is empty.";

            if (entry.Key.Length < ApiKeyOptions.MinimumKeyLength)
                return $"Auth:ApiKeys[{index}]:Key must be at least {ApiKeyOptions.MinimumKeyLength} characters.";

            if (!seen.Add(entry.Key))
                return $"Auth:ApiKeys[{index}]:Key is a duplicate of an earlier entry.";

            if (entry.ExpiresAt is { } expiry && expiry <= now)
                return $"Auth:ApiKeys[{index}]:ExpiresAt is in the past ({expiry:O}) — remove the entry or extend the expiry.";

            index++;
        }

        return null;
    }
}
