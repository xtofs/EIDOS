using Eidos.Core;
using Microsoft.AspNetCore.Routing;

namespace Eidos.AspNetCore;

public static class EidosEndpointRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapEidos(
        this IEndpointRouteBuilder endpoints,
        EidosDocumentSyntax document,
        Action<EidosMapBuilder> configure,
        Action<EidosRouteMappingOptions>? configureOptions = null,
        IEidosOperationPolicy? operationPolicy = null)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new EidosRouteMappingOptions();
        configureOptions?.Invoke(options);

        var builder = new EidosMapBuilder(endpoints, document, options, operationPolicy ?? new DefaultEidosOperationPolicy());
        configure(builder);
        builder.ValidateCoverage();

        return endpoints;
    }

    public static EidosMapBuilder CreateEidosMapBuilder(
        this IEndpointRouteBuilder endpoints,
        EidosDocumentSyntax document,
        Action<EidosRouteMappingOptions>? configureOptions = null,
        IEidosOperationPolicy? operationPolicy = null)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(document);

        var options = new EidosRouteMappingOptions();
        configureOptions?.Invoke(options);
        return new EidosMapBuilder(endpoints, document, options, operationPolicy ?? new DefaultEidosOperationPolicy());
    }
}
