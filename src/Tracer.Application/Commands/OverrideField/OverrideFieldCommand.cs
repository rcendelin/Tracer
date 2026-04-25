using MediatR;
using Tracer.Domain.Enums;

namespace Tracer.Application.Commands.OverrideField;

/// <summary>
/// Manually overrides a single string-typed field on a CKB profile (B-85).
/// Caller identity is server-derived from the API key middleware and passed
/// in as <see cref="CallerFingerprint"/> — never read from the request body.
/// </summary>
/// <param name="ProfileId">The target CKB profile.</param>
/// <param name="Field">Which <see cref="FieldName"/> to override (string-typed only — see whitelist in handler).</param>
/// <param name="NewValue">The new value supplied by the operator.</param>
/// <param name="Reason">Free-text justification (logged, not persisted in this version — see plan §6).</param>
/// <param name="CallerFingerprint">Server-derived caller fingerprint, e.g. <c>"apikey:1f2a3b4c"</c>.</param>
/// <param name="CallerLabel">Configured key label, e.g. <c>"fieldforce-prod"</c>; informational only.</param>
public sealed record OverrideFieldCommand(
    Guid ProfileId,
    FieldName Field,
    string NewValue,
    string Reason,
    string CallerFingerprint,
    string? CallerLabel) : IRequest<OverrideFieldResult>;

/// <summary>
/// Outcome of an <see cref="OverrideFieldCommand"/>.
/// </summary>
public enum OverrideFieldResult
{
    /// <summary>Override applied; a `ChangeEvent` was produced and persisted.</summary>
    Overridden = 0,

    /// <summary>Value already matched the supplied input; no change recorded (idempotent).</summary>
    NoChange = 1,

    /// <summary>No profile with the given id exists.</summary>
    ProfileNotFound = 2,

    /// <summary>The requested <see cref="FieldName"/> is not eligible for manual override.</summary>
    FieldNotOverridable = 3,
}
