using Eidos.Core;
using Eidos.Core.OpenApi;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.Swagger;

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

    public static WebApplicationBuilder AddEidosOpenApiAndReDoc(
        this WebApplicationBuilder builder,
        EidosDocumentSyntax document,
        ApiInfo info,
        Action<EidosOpenApiRouteOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(info);

        var options = new EidosOpenApiRouteOptions();
        configure?.Invoke(options);

        var openApiDocument = OpenApiDocumentFactory.Create(OpenApiModelGenerator.Generate(document, info));
        builder.Services.AddSingleton(openApiDocument);
        builder.Services.AddSingleton<ISwaggerProvider, EidosOpenApiSwaggerProvider>();
        builder.Services.AddSingleton(options);

        return builder;
    }

    public static WebApplication MapEidosOpenApiAndReDoc(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var options = app.Services.GetRequiredService<EidosOpenApiRouteOptions>();

        app.MapGet(options.OpenApiPath, (OpenApiDocument document) =>
        {
            using var stringWriter = new StringWriter();
            document.SerializeAsV3(new OpenApiJsonWriter(stringWriter));
            return Results.Text(stringWriter.ToString(), "application/json");
        });

        app.MapGet(options.ReDocPath, () =>
        {
            var html = ReDocHtmlTemplate
                .Replace("__OPENAPI_PATH__", options.OpenApiPath, StringComparison.Ordinal);

            return Results.Content(html, "text/html");
        });

        return app;
    }

    const string ReDocHtmlTemplate = """
        <!DOCTYPE html>
        <html>
          <head>
            <title>API Documentation</title>
            <meta charset="utf-8" />
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <style>body { margin: 0; padding: 0; }</style>
          </head>
          <body>
            <redoc spec-url="__OPENAPI_PATH__"></redoc>
            <script src="https://cdn.redoc.ly/redoc/latest/bundles/redoc.standalone.js"> </script>
          </body>
        </html>
        """;
}

public sealed class EidosOpenApiRouteOptions
{
    public string OpenApiPath { get; set; } = "/swagger/v1/swagger.json";

    public string ReDocPath { get; set; } = "/redoc";
}

internal sealed class EidosOpenApiSwaggerProvider(OpenApiDocument document) : ISwaggerProvider
{
    private readonly OpenApiDocument _document = document;

    public OpenApiDocument GetSwagger(string documentName, string? host = null, string? basePath = null)
    {
        return _document;
    }
}
