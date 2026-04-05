using Tracer.Domain.Enums;

namespace Tracer.Application.DTOs;

/// <summary>
/// Input DTO for submitting an enrichment request via the API.
/// </summary>
public sealed record TraceRequestDto
{
    public string? CompanyName { get; init; }
    public string? Phone { get; init; }
    public string? Email { get; init; }
    public string? Website { get; init; }
    public string? Address { get; init; }
    public string? City { get; init; }
    public string? Country { get; init; }
    public string? RegistrationId { get; init; }
    public string? TaxId { get; init; }
    public string? IndustryHint { get; init; }
    public TraceDepth Depth { get; init; } = TraceDepth.Standard;
    public Uri? CallbackUrl { get; init; }
}
