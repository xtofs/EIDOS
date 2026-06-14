using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Eidos.AspNetCore;

/// <summary>
/// The per-resource information needed to decorate a representation with reserved fields:
/// the type name (<c>_type</c>), the collection segment (for <c>_self</c>), and whether the resource has a
/// lifecycle (whether <c>_state</c> applies).
/// </summary>
internal readonly record struct ResourceContext(string TypeName, string CollectionSegment, bool HasLifecycle);

internal static class RepresentationWriter
{
    /// <summary>Wraps an <see cref="IEidosRepresentation"/> in a result that enriches it at execution time.</summary>
    public static IResult Write(IEidosRepresentation representation, ResourceContext context) =>
        new EnrichedRepresentationResult(representation, context);

    private sealed class EnrichedRepresentationResult(IEidosRepresentation representation, ResourceContext context) : IResult
    {
        public async Task ExecuteAsync(HttpContext httpContext)
        {
            foreach (var header in representation.Headers)
            {
                httpContext.Response.Headers.Append(header.Key, header.Value);
            }

            // Errors / redirects / anything non-representation pass straight through.
            if (representation.PassThrough is { } passThrough)
            {
                await passThrough.ExecuteAsync(httpContext).ConfigureAwait(false);
                return;
            }

            if (representation.Location is { } location)
            {
                httpContext.Response.Headers.Location = location;
            }

            if (representation.Value is null)
            {
                await Results.StatusCode(representation.StatusCode).ExecuteAsync(httpContext).ConfigureAwait(false);
                return;
            }

            // Serialize with the app's configured options (camelCase + enum-as-string), then inject reserved fields.
            var options = httpContext.RequestServices.GetService<IOptions<JsonOptions>>()?.Value.SerializerOptions
                ?? JsonSerializerOptions.Web;

            var node = JsonSerializer.SerializeToNode(representation.Value, representation.Value.GetType(), options);
            Enrich(node, context);

            await Results.Json(node, options, statusCode: representation.StatusCode).ExecuteAsync(httpContext).ConfigureAwait(false);
        }
    }

    private static void Enrich(JsonNode? node, ResourceContext context)
    {
        switch (node)
        {
            case JsonArray array:
                foreach (var item in array)
                {
                    EnrichObject(item as JsonObject, context);
                }

                break;
            case JsonObject obj:
                EnrichObject(obj, context);
                break;
        }
    }

    // Reserved system fields (spec §2.1): _self (canonical URL), _type (TypeName), _state (current state,
    // lifecycle-bearing types only). key/state are located by the V0.1 camelCase serialized-property convention.
    private static void EnrichObject(JsonObject? obj, ResourceContext context)
    {
        if (obj is null)
        {
            return;
        }

        obj["_type"] = context.TypeName;

        if (obj.TryGetPropertyValue("key", out var keyNode) && keyNode is not null)
        {
            obj["_self"] = $"/{context.CollectionSegment}/{keyNode.GetValue<string>()}";
        }

        if (context.HasLifecycle && obj.TryGetPropertyValue("state", out var stateNode) && stateNode is not null)
        {
            obj["_state"] = stateNode.GetValue<string>();
        }
    }
}
