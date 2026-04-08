using FluentAssertions;
using Tracer.Application.DTOs;
using Tracer.Application.Mapping;
using Tracer.Contracts.Messages;
using ContractsEnums = Tracer.Contracts.Enums;
using DomainEnums = Tracer.Domain.Enums;

namespace Tracer.Application.Tests.Messaging;

/// <summary>
/// Verifies round-trip field mapping between Contracts message types and internal Application DTOs.
/// These mappings are used by <c>ServiceBusConsumer</c> (inbound) and the batch handler (outbound).
/// </summary>
public sealed class ContractMappingRoundTripTests
{
    // ── ToTraceRequestDto ──────────────────────────────────────────────────────

    [Fact]
    public void ToTraceRequestDto_NullMessage_ThrowsArgumentNullException()
    {
        TraceRequestMessage message = null!;

        var act = () => message.ToTraceRequestDto();

        act.Should().Throw<ArgumentNullException>().WithParameterName("message");
    }

    [Fact]
    public void ToTraceRequestDto_AllFields_MapsCorrectly()
    {
        var message = new TraceRequestMessage
        {
            CorrelationId = "corr-001",
            CompanyName = "ACME s.r.o.",
            RegistrationId = "00177041",
            TaxId = "CZ00177041",
            Phone = "+420123456789",
            Email = "info@acme.cz",
            Website = "https://acme.cz",
            Address = "Václavské nám. 1",
            City = "Praha",
            Country = "CZ",
            IndustryHint = "automotive",
            Depth = ContractsEnums.TraceDepth.Deep,
            Source = "fieldforce-crm",
        };

        var dto = message.ToTraceRequestDto();

        dto.CompanyName.Should().Be("ACME s.r.o.");
        dto.RegistrationId.Should().Be("00177041");
        dto.TaxId.Should().Be("CZ00177041");
        dto.Phone.Should().Be("+420123456789");
        dto.Email.Should().Be("info@acme.cz");
        dto.Website.Should().Be("https://acme.cz");
        dto.Address.Should().Be("Václavské nám. 1");
        dto.City.Should().Be("Praha");
        dto.Country.Should().Be("CZ");
        dto.IndustryHint.Should().Be("automotive");
        dto.Depth.Should().Be(DomainEnums.TraceDepth.Deep);
    }

    [Fact]
    public void ToTraceRequestDto_MinimalMessage_MapsDefaults()
    {
        var message = new TraceRequestMessage
        {
            CorrelationId = "corr-002",
            CompanyName = "Škoda Auto a.s.",
        };

        var dto = message.ToTraceRequestDto();

        dto.CompanyName.Should().Be("Škoda Auto a.s.");
        dto.RegistrationId.Should().BeNull();
        dto.Country.Should().BeNull();
        dto.Depth.Should().Be(DomainEnums.TraceDepth.Standard); // TraceDepth.Standard = 1 in both enums
    }

    [Theory]
    [InlineData(ContractsEnums.TraceDepth.Quick, DomainEnums.TraceDepth.Quick)]
    [InlineData(ContractsEnums.TraceDepth.Standard, DomainEnums.TraceDepth.Standard)]
    [InlineData(ContractsEnums.TraceDepth.Deep, DomainEnums.TraceDepth.Deep)]
    public void ToTraceRequestDto_AllDepthValues_MapByIntegerValue(
        ContractsEnums.TraceDepth contractDepth, DomainEnums.TraceDepth expectedDomainDepth)
    {
        var message = new TraceRequestMessage
        {
            CorrelationId = "corr-depth",
            Depth = contractDepth,
        };

        var dto = message.ToTraceRequestDto();

        dto.Depth.Should().Be(expectedDomainDepth);
    }

    // ── ToResponseMessage ─────────────────────────────────────────────────────

    [Fact]
    public void ToResponseMessage_NullDto_ThrowsArgumentNullException()
    {
        TraceResultDto dto = null!;

        var act = () => dto.ToResponseMessage("corr-001");

        act.Should().Throw<ArgumentNullException>().WithParameterName("dto");
    }

    [Fact]
    public void ToResponseMessage_EmptyCorrelationId_ThrowsArgumentException()
    {
        var dto = BuildMinimalResultDto(DomainEnums.TraceStatus.Failed);

        var act = () => dto.ToResponseMessage(string.Empty);

        act.Should().Throw<ArgumentException>().WithParameterName("correlationId");
    }

    [Fact]
    public void ToResponseMessage_WhitespaceCorrelationId_ThrowsArgumentException()
    {
        var dto = BuildMinimalResultDto(DomainEnums.TraceStatus.Failed);

        var act = () => dto.ToResponseMessage("   ");

        act.Should().Throw<ArgumentException>().WithParameterName("correlationId");
    }

    [Fact]
    public void ToResponseMessage_CompletedResult_MapsAllFields()
    {
        var traceId = Guid.NewGuid();
        var createdAt = DateTimeOffset.UtcNow.AddSeconds(-2);
        var completedAt = DateTimeOffset.UtcNow;
        var dto = new TraceResultDto
        {
            TraceId = traceId,
            Status = DomainEnums.TraceStatus.Completed,
            OverallConfidence = 0.93,
            CreatedAt = createdAt,
            CompletedAt = completedAt,
            DurationMs = 1234,
            FailureReason = null,
        };
        const string correlationId = "ff-req-00042";

        var response = dto.ToResponseMessage(correlationId);

        response.TraceId.Should().Be(traceId);
        response.CorrelationId.Should().Be(correlationId);
        response.Status.Should().Be(ContractsEnums.TraceStatus.Completed);
        response.OverallConfidence.Should().Be(0.93);
        response.CreatedAt.Should().Be(createdAt);
        response.CompletedAt.Should().Be(completedAt);
        response.DurationMs.Should().Be(1234);
        response.FailureReason.Should().BeNull();
    }

    [Fact]
    public void ToResponseMessage_FailedResult_MapsStatusAndReason()
    {
        var dto = BuildMinimalResultDto(DomainEnums.TraceStatus.Failed) with
        {
            FailureReason = "All providers failed",
            OverallConfidence = null,
        };

        var response = dto.ToResponseMessage("corr-fail");

        response.Status.Should().Be(ContractsEnums.TraceStatus.Failed);
        response.FailureReason.Should().Be("All providers failed");
        response.OverallConfidence.Should().BeNull();
        response.CorrelationId.Should().Be("corr-fail");
    }

    [Theory]
    [InlineData(DomainEnums.TraceStatus.Pending, ContractsEnums.TraceStatus.Pending)]
    [InlineData(DomainEnums.TraceStatus.InProgress, ContractsEnums.TraceStatus.InProgress)]
    [InlineData(DomainEnums.TraceStatus.Completed, ContractsEnums.TraceStatus.Completed)]
    [InlineData(DomainEnums.TraceStatus.PartiallyCompleted, ContractsEnums.TraceStatus.PartiallyCompleted)]
    [InlineData(DomainEnums.TraceStatus.Failed, ContractsEnums.TraceStatus.Failed)]
    [InlineData(DomainEnums.TraceStatus.Cancelled, ContractsEnums.TraceStatus.Cancelled)]
    [InlineData(DomainEnums.TraceStatus.Queued, ContractsEnums.TraceStatus.Queued)]
    public void ToResponseMessage_AllStatusValues_MapByIntegerValue(
        DomainEnums.TraceStatus domainStatus,
        ContractsEnums.TraceStatus expectedContractStatus)
    {
        var dto = BuildMinimalResultDto(domainStatus);

        var response = dto.ToResponseMessage("corr-status");

        response.Status.Should().Be(expectedContractStatus);
    }

    // ── ToContract (ChangeEventDto) ───────────────────────────────────────────

    [Fact]
    public void ToContractChangeEvent_NullDto_ThrowsArgumentNullException()
    {
        ChangeEventDto dto = null!;

        var act = () => dto.ToContract();

        act.Should().Throw<ArgumentNullException>().WithParameterName("dto");
    }

    [Fact]
    public void ToContractChangeEvent_ValidDto_MapsAllFields()
    {
        var id = Guid.NewGuid();
        var detectedAt = DateTimeOffset.UtcNow;
        var dto = new ChangeEventDto
        {
            Id = id,
            CompanyProfileId = Guid.NewGuid(),
            Field = DomainEnums.FieldName.EntityStatus,
            ChangeType = DomainEnums.ChangeType.Updated,
            Severity = DomainEnums.ChangeSeverity.Critical,
            PreviousValueJson = "\"Active\"",
            NewValueJson = "\"Dissolved\"",
            DetectedBy = "ares",
            DetectedAt = detectedAt,
            IsNotified = false,
        };

        var contract = dto.ToContract();

        contract.Id.Should().Be(id);
        contract.Field.Should().Be(ContractsEnums.FieldName.EntityStatus);
        contract.ChangeType.Should().Be(ContractsEnums.ChangeType.Updated);
        contract.Severity.Should().Be(ContractsEnums.ChangeSeverity.Critical);
        contract.PreviousValueJson.Should().Be("\"Active\"");
        contract.NewValueJson.Should().Be("\"Dissolved\"");
        contract.DetectedBy.Should().Be("ares");
        contract.DetectedAt.Should().Be(detectedAt);
    }

    // ── MapEnum drift detection ───────────────────────────────────────────────

    [Fact]
    public void ToResponseMessage_UndefinedDomainStatus_ThrowsInvalidOperationException()
    {
        // Cast an out-of-range integer to TraceStatus to simulate a Domain enum member
        // that has no matching Contracts enum member (drift scenario).
        var dto = BuildMinimalResultDto((DomainEnums.TraceStatus)99);

        var act = () => dto.ToResponseMessage("corr-drift");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*TraceStatus*99*");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static TraceResultDto BuildMinimalResultDto(DomainEnums.TraceStatus status) =>
        new()
        {
            TraceId = Guid.NewGuid(),
            Status = status,
            CreatedAt = DateTimeOffset.UtcNow,
        };
}
