using Microsoft.Extensions.Options;

namespace Tracer.Api.Middleware;

/// <summary>
/// Validates <see cref="ApiKeyOptions"/> at startup. Delegates to
/// <see cref="ApiKeyOptionsBinder.Validate"/>, plumbing through the registered
/// <see cref="TimeProvider"/> so that "already expired" diagnostics honour
/// any test clock override.
/// </summary>
internal sealed class ApiKeyOptionsValidator : IValidateOptions<ApiKeyOptions>
{
    private readonly TimeProvider _timeProvider;

    public ApiKeyOptionsValidator(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    public ValidateOptionsResult Validate(string? name, ApiKeyOptions options)
    {
        var error = ApiKeyOptionsBinder.Validate(options, _timeProvider.GetUtcNow());
        return error is null
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(error);
    }
}
