using MediatR;
using Tracer.Domain.Interfaces;

namespace Tracer.Application.Commands.AcknowledgeChange;

/// <summary>
/// Handles <see cref="AcknowledgeChangeCommand"/> by toggling the
/// <c>IsNotified</c> flag on the change event. The operation is idempotent —
/// <see cref="Tracer.Domain.Entities.ChangeEvent.MarkNotified"/> is safe to
/// call repeatedly.
/// </summary>
public sealed class AcknowledgeChangeHandler : IRequestHandler<AcknowledgeChangeCommand, AcknowledgeChangeResult>
{
    private readonly IChangeEventRepository _repository;
    private readonly IUnitOfWork _unitOfWork;

    public AcknowledgeChangeHandler(IChangeEventRepository repository, IUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task<AcknowledgeChangeResult> Handle(
        AcknowledgeChangeCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var changeEvent = await _repository.GetByIdAsync(request.ChangeEventId, cancellationToken)
            .ConfigureAwait(false);

        if (changeEvent is null)
            return AcknowledgeChangeResult.NotFound;

        // Idempotent: MarkNotified() is a no-op when already set.
        changeEvent.MarkNotified();
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return AcknowledgeChangeResult.Acknowledged;
    }
}
