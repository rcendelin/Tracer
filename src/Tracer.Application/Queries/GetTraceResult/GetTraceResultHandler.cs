using MediatR;
using Tracer.Application.DTOs;
using Tracer.Application.Mapping;
using Tracer.Domain.Interfaces;

namespace Tracer.Application.Queries.GetTraceResult;

/// <summary>
/// Handles <see cref="GetTraceResultQuery"/> by loading the trace request and its associated profile.
/// </summary>
public sealed class GetTraceResultHandler : IRequestHandler<GetTraceResultQuery, TraceResultDto?>
{
    private readonly ITraceRequestRepository _traceRequestRepository;
    private readonly ICompanyProfileRepository _profileRepository;

    public GetTraceResultHandler(
        ITraceRequestRepository traceRequestRepository,
        ICompanyProfileRepository profileRepository)
    {
        _traceRequestRepository = traceRequestRepository;
        _profileRepository = profileRepository;
    }

    public async Task<TraceResultDto?> Handle(GetTraceResultQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var traceRequest = await _traceRequestRepository.GetByIdAsync(request.TraceId, cancellationToken)
            .ConfigureAwait(false);

        if (traceRequest is null)
            return null;

        var profile = traceRequest.CompanyProfileId.HasValue
            ? await _profileRepository.GetByIdAsync(traceRequest.CompanyProfileId.Value, cancellationToken)
                .ConfigureAwait(false)
            : null;

        return traceRequest.ToResultDto(profile);
    }
}
