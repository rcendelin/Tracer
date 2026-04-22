using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi.Models;

namespace Tracer.Api.OpenApi;

/// <summary>
/// Adds the <see cref="TracerOpenApiDocumentTransformer.ApiKeySchemeName"/>
/// security requirement to every operation except the public allowlist
/// (<c>/health</c>, <c>/openapi/*</c>). This mirrors the whitelist in
/// <see cref="Middleware.ApiKeyAuthMiddleware"/> so the spec reflects
/// runtime behaviour for hosts with configured API keys (B-82).
/// </summary>
/// <remarks>
/// In Development mode with no configured keys, the middleware passes all
/// requests through — the spec deliberately still advertises the requirement
/// because the spec describes the contract, not the runtime relaxation.
/// </remarks>
internal sealed class ApiKeySecurityRequirementTransformer : IOpenApiOperationTransformer
{
    private static readonly string[] AnonymousPathPrefixes = ["/health", "/openapi"];

    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(context);

        var path = context.Description.RelativePath is null
            ? string.Empty
            : "/" + context.Description.RelativePath;

        if (IsAnonymous(path))
            return Task.CompletedTask;

        operation.Security ??= [];
        operation.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Id = TracerOpenApiDocumentTransformer.ApiKeySchemeName,
                    Type = ReferenceType.SecurityScheme,
                },
            }] = [],
        });

        return Task.CompletedTask;
    }

    private static bool IsAnonymous(string path)
    {
        foreach (var prefix in AnonymousPathPrefixes)
        {
            if (path.Equals(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
            if (path.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
