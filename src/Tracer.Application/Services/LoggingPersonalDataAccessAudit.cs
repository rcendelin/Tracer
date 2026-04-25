using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tracer.Domain.Enums;

namespace Tracer.Application.Services;

/// <summary>
/// Default <see cref="IPersonalDataAccessAudit"/> — writes a structured log
/// entry per access. Honours <see cref="GdprOptions.AuditPersonalDataAccess"/>
/// so the audit channel can be suppressed in local development without
/// removing call sites.
/// </summary>
internal sealed partial class LoggingPersonalDataAccessAudit : IPersonalDataAccessAudit
{
    private readonly ILogger<LoggingPersonalDataAccessAudit> _logger;
    private readonly bool _enabled;

    public LoggingPersonalDataAccessAudit(
        ILogger<LoggingPersonalDataAccessAudit> logger,
        IOptions<GdprOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _logger = logger;
        _enabled = options.Value.AuditPersonalDataAccess;
    }

    public void RecordAccess(Guid profileId, FieldName field, string accessor, string purpose)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessor);
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);

        if (!_enabled)
            return;

        LogPersonalDataAccess(profileId, field, accessor, purpose);
    }

    [LoggerMessage(
        EventId = 9001,
        Level = LogLevel.Information,
        Message = "GDPR personal-data access: profile={ProfileId} field={Field} accessor={Accessor} purpose={Purpose}")]
    private partial void LogPersonalDataAccess(Guid profileId, FieldName field, string accessor, string purpose);
}
