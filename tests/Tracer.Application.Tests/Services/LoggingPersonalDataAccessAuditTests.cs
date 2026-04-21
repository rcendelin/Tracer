using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Tracer.Application.Services;
using Tracer.Domain.Enums;

namespace Tracer.Application.Tests.Services;

/// <summary>
/// Tests for <see cref="LoggingPersonalDataAccessAudit"/> — the default audit
/// hook that emits a structured log entry per personal-data read.
/// </summary>
public sealed class LoggingPersonalDataAccessAuditTests
{
    private static LoggingPersonalDataAccessAudit CreateSut(
        ILogger<LoggingPersonalDataAccessAudit> logger,
        bool auditEnabled = true) =>
        new(logger, Options.Create(new GdprOptions { AuditPersonalDataAccess = auditEnabled }));

    // ── Emission ────────────────────────────────────────────────────────

    [Fact]
    public void RecordAccess_WhenEnabled_WritesOneInformationLog()
    {
        var logger = new CollectingLogger<LoggingPersonalDataAccessAudit>();
        var sut = CreateSut(logger);
        var profileId = Guid.NewGuid();

        sut.RecordAccess(profileId, FieldName.Officers, "svc-fieldforce", "profile-detail-endpoint");

        logger.Entries.Should().ContainSingle();
        var entry = logger.Entries[0];
        entry.Level.Should().Be(LogLevel.Information);
        entry.Message.Should().Contain(profileId.ToString());
        entry.Message.Should().Contain("Officers");
        entry.Message.Should().Contain("svc-fieldforce");
        entry.Message.Should().Contain("profile-detail-endpoint");
    }

    [Fact]
    public void RecordAccess_WhenDisabled_EmitsNothing()
    {
        var logger = new CollectingLogger<LoggingPersonalDataAccessAudit>();
        var sut = CreateSut(logger, auditEnabled: false);

        sut.RecordAccess(Guid.NewGuid(), FieldName.Officers, "svc", "purpose");

        logger.Entries.Should().BeEmpty();
    }

    // ── Guard clauses ───────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void RecordAccess_InvalidAccessor_Throws(string? accessor)
    {
        var sut = CreateSut(NullLogger<LoggingPersonalDataAccessAudit>.Instance);
        var act = () => sut.RecordAccess(Guid.NewGuid(), FieldName.Officers, accessor!, "purpose");
        act.Should().Throw<ArgumentException>().WithParameterName("accessor");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void RecordAccess_InvalidPurpose_Throws(string? purpose)
    {
        var sut = CreateSut(NullLogger<LoggingPersonalDataAccessAudit>.Instance);
        var act = () => sut.RecordAccess(Guid.NewGuid(), FieldName.Officers, "svc", purpose!);
        act.Should().Throw<ArgumentException>().WithParameterName("purpose");
    }

    [Fact]
    public void RecordAccess_InvalidAccessor_DoesNotEmitEvenWhenDisabled()
    {
        // Guard clauses run before the disabled check so callers are always
        // forced to pass valid arguments, independent of config.
        var logger = new CollectingLogger<LoggingPersonalDataAccessAudit>();
        var sut = CreateSut(logger, auditEnabled: false);
        var act = () => sut.RecordAccess(Guid.NewGuid(), FieldName.Officers, " ", "purpose");
        act.Should().Throw<ArgumentException>();
        logger.Entries.Should().BeEmpty();
    }

    [Fact]
    public void Ctor_NullOptions_Throws()
    {
        var act = () => new LoggingPersonalDataAccessAudit(
            NullLogger<LoggingPersonalDataAccessAudit>.Instance, null!);
        act.Should().Throw<ArgumentNullException>();
    }

    // ── Test logger ─────────────────────────────────────────────────────

    private sealed class CollectingLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = new();

        IDisposable? ILogger.BeginScope<TState>(TState state) => null;

        bool ILogger.IsEnabled(LogLevel logLevel) => true;

        void ILogger.Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add(new LogEntry(logLevel, eventId, formatter(state, exception), exception));
        }
    }

    private sealed record LogEntry(LogLevel Level, EventId EventId, string Message, Exception? Exception);
}
