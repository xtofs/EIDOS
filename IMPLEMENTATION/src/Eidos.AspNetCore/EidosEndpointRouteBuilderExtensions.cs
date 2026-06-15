using Eidos.Core;
using Eidos.Core.OpenApi;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.Swagger;

namespace Eidos.AspNetCore;

public static class EidosEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Maps handlers for the given Eidos document and validates route coverage.
    /// If <see cref="EidosOpenApiRouteOptions"/> is registered in DI, the metadata endpoint
    /// is also mapped at <see cref="EidosOpenApiRouteOptions.MetadataPath"/>.
    /// </summary>
    public static IEndpointRouteBuilder MapEidosHandlers(
        this IEndpointRouteBuilder endpoints,
        EidosDocumentSyntax document,
        Action<EidosRouteBuilder> configure,
        Action<EidosRouteMappingOptions>? configureOptions = null,
        IEidosOperationPolicy? operationPolicy = null)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(configure);

        var routeMappingOptions = new EidosRouteMappingOptions();
        configureOptions?.Invoke(routeMappingOptions);

        var builder = new EidosRouteBuilder(endpoints, document, routeMappingOptions, operationPolicy ?? new DefaultEidosOperationPolicy());
        configure(builder);

        var openApiRouteOptions = endpoints.ServiceProvider.GetService<EidosOpenApiRouteOptions>();
        var metadataOptions = endpoints.ServiceProvider.GetService<EidosMetadataOptions>();
        var metadataPath = metadataOptions?.MetadataPath ?? "/routes";
        builder.MapMetadataEndpoint(metadataPath);

        builder.ValidateCoverage();
        builder.Build();

        return endpoints;
    }

    public static EidosRouteBuilder CreateEidosMapBuilder(
        this IEndpointRouteBuilder endpoints,
        EidosDocumentSyntax document,
        Action<EidosRouteMappingOptions>? configureOptions = null,
        IEidosOperationPolicy? operationPolicy = null)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(document);

        var options = new EidosRouteMappingOptions();
        configureOptions?.Invoke(options);
        return new EidosRouteBuilder(endpoints, document, options, operationPolicy ?? new DefaultEidosOperationPolicy());
    }

    public static WebApplicationBuilder AddEidosOpenApi(
        this WebApplicationBuilder builder,
        IConfigurationSection optionsSection,
        Action<EidosOpenApiOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(optionsSection);

        var options = new EidosOpenApiOptions();
        optionsSection.Bind(options);
        configure?.Invoke(options);

        return builder.AddEidosOpenApi(options);
    }

    public static WebApplicationBuilder AddEidosMetadata(
        this WebApplicationBuilder builder,
        IConfigurationSection optionsSection,
        Action<EidosMetadataOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(optionsSection);

        var options = new EidosMetadataOptions();
        optionsSection.Bind(options);
        configure?.Invoke(options);

        return builder.AddEidosMetadata(options);
    }

    public static WebApplicationBuilder AddEidosMetadata(
        this WebApplicationBuilder builder,
        EidosMetadataOptions options)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(options);

        builder.Services.AddSingleton(options);
        return builder;
    }

    public static WebApplicationBuilder AddEidosOpenApi(
        this WebApplicationBuilder builder,
        string title,
        string version,
        Action<EidosOpenApiRouteOptions>? configure = null)
    {
        return builder.AddEidosOpenApi(new ApiInfo(title, version), configure);
    }

    public static WebApplicationBuilder AddEidosOpenApi(
        this WebApplicationBuilder builder,
        EidosOpenApiOptions options)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(options);

        return AddEidosOpenApiInternal(builder, static services => services.GetRequiredService<EidosDocumentSyntax>(), new ApiInfo(options.Title, options.Version), options.Routes);
    }

    public static WebApplicationBuilder AddEidosOpenApi(
        this WebApplicationBuilder builder,
        string title,
        string version,
        IConfigurationSection routeOptionsSection,
        Action<EidosOpenApiRouteOptions>? configure = null)
    {
        return builder.AddEidosOpenApi(new ApiInfo(title, version), routeOptionsSection, configure);
    }

    public static WebApplicationBuilder AddEidosOpenApi(
        this WebApplicationBuilder builder,
        EidosDocumentSyntax document,
        IConfigurationSection optionsSection,
        Action<EidosOpenApiOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(optionsSection);

        var options = new EidosOpenApiOptions();
        optionsSection.Bind(options);
        configure?.Invoke(options);

        return AddEidosOpenApiInternal(builder, _ => document, new ApiInfo(options.Title, options.Version), options.Routes);
    }

    public static WebApplicationBuilder AddEidosOpenApi(
        this WebApplicationBuilder builder,
        EidosDocumentSyntax document,
        string Title, string Version,
        Action<EidosOpenApiRouteOptions>? configure = null)
    {
        var routeOptions = new EidosOpenApiRouteOptions();
        configure?.Invoke(routeOptions);

        return AddEidosOpenApiInternal(builder, _ => document, new ApiInfo(Title, Version), routeOptions);
    }

    public static WebApplicationBuilder AddEidosOpenApi(
        this WebApplicationBuilder builder,
        EidosDocumentSyntax document,
        EidosOpenApiOptions options)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(options);

        return AddEidosOpenApiInternal(builder, _ => document, new ApiInfo(options.Title, options.Version), options.Routes);
    }

    public static WebApplicationBuilder AddEidosOpenApi(
        this WebApplicationBuilder builder,
        ApiInfo info,
        Action<EidosOpenApiRouteOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(info);

        var routeOptions = new EidosOpenApiRouteOptions();
        configure?.Invoke(routeOptions);

        return AddEidosOpenApiInternal(builder, static services => services.GetRequiredService<EidosDocumentSyntax>(), info, routeOptions);
    }

    public static WebApplicationBuilder AddEidosOpenApi(
        this WebApplicationBuilder builder,
        ApiInfo info,
        IConfigurationSection routeOptionsSection,
        Action<EidosOpenApiRouteOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(routeOptionsSection);

        var routeOptions = new EidosOpenApiRouteOptions();
        routeOptionsSection.Bind(routeOptions);
        configure?.Invoke(routeOptions);

        return AddEidosOpenApiInternal(builder, static services => services.GetRequiredService<EidosDocumentSyntax>(), info, routeOptions);
    }

    /// <summary>
    /// Adds the OpenAPI document generated from the given Eidos document to the DI container, 
    /// and configures the endpoints for serving the OpenAPI JSON and ReDoc UI based on the given options. 
    /// The OpenAPI document is generated once at startup and cached in a singleton service. 
    /// The UI endpoint is served at <c>/redoc</c> by default, and can be customized 
    /// via the <c>UiPath</c> property of <see cref="EidosOpenApiRouteOptions"/>. 
    /// The OpenAPI JSON is served at <c>/swagger/v1/swagger.json</c> by default, and can be customized 
    /// via the <c>OpenApiPath</c> property of <see cref="EidosOpenApiRouteOptions"/>.
    /// </summary>
    /// <param name="builder"></param>
    /// <param name="document"></param>
    /// <param name="info"></param>
    /// <param name="configure"></param>
    /// <returns></returns>
    public static WebApplicationBuilder AddEidosOpenApi(
        this WebApplicationBuilder builder,
        EidosDocumentSyntax document,
        ApiInfo info,
        Action<EidosOpenApiRouteOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(document);

        var routeOptions = new EidosOpenApiRouteOptions();
        configure?.Invoke(routeOptions);

        return AddEidosOpenApiInternal(builder, _ => document, info, routeOptions);
    }

    private static WebApplicationBuilder AddEidosOpenApiInternal(
        this WebApplicationBuilder builder,
        Func<IServiceProvider, EidosDocumentSyntax> documentFactory,
        ApiInfo info,
        EidosOpenApiRouteOptions routeOptions)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(documentFactory);
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(routeOptions);

        builder.Services.AddSingleton(sp => OpenApiDocumentGenerator.Generate(documentFactory(sp), info));
        builder.Services.AddSingleton<ISwaggerProvider, EidosOpenApiSwaggerProvider>();
        builder.Services.AddSingleton(routeOptions);

        return builder;
    }

    /// <summary>
    /// Maps the full Eidos HTTP surface: schema-driven handlers, metadata endpoint,
    /// OpenAPI JSON endpoint, and ReDoc UI endpoint.
    /// </summary>
    public static IEndpointRouteBuilder MapEidosRoutes(
        this IEndpointRouteBuilder endpoints,
        EidosDocumentSyntax document,
        Action<EidosRouteBuilder> configure,
        Action<EidosRouteMappingOptions>? configureOptions = null,
        IEidosOperationPolicy? operationPolicy = null)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var builder = endpoints.CreateEidosMapBuilder(document, configureOptions, operationPolicy);
        configure(builder);
        builder.MapEidosRoutes();

        return endpoints;
    }

    /// <summary>
    /// Finishes a builder configured via <see cref="CreateEidosMapBuilder"/>: maps the metadata endpoint,
    /// validates route coverage, commits the deferred route mappings, and serves the OpenAPI JSON and ReDoc UI
    /// endpoints. Lets the route configuration read as two statements instead of nested trailing lambdas.
    /// </summary>
    public static EidosRouteBuilder MapEidosRoutes(this EidosRouteBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var endpoints = builder.Endpoints;
        var routeOptions = endpoints.ServiceProvider.GetRequiredService<EidosOpenApiRouteOptions>();
        var metadataOptions = endpoints.ServiceProvider.GetService<EidosMetadataOptions>();
        var metadataPath = metadataOptions?.MetadataPath ?? "/routes";

        builder.MapMetadataEndpoint(metadataPath);
        builder.ValidateCoverage();
        builder.Build();

        endpoints.MapGet(routeOptions.OpenApiPath, (OpenApiDocument openApiDocument) =>
        {
            using var stringWriter = new StringWriter();
            openApiDocument.SerializeAsV3(new OpenApiJsonWriter(stringWriter));
            return Results.Text(stringWriter.ToString(), "application/json");
        });

        endpoints.MapGet(routeOptions.UiPath, () =>
        {
            var html = ReDocHtmlTemplate
                .Replace("__OPENAPI_PATH__", routeOptions.OpenApiPath, StringComparison.Ordinal);

            return Results.Content(html, "text/html");
        });

        return builder;
    }

    /// <summary>
    /// Adds the endpoints for serving the OpenAPI JSON and ReDoc UI based on the Eidos document and options configured in DI.
    /// </summary>
    /// <param name="app"></param>
    /// <returns></returns>
    public static WebApplication MapEidosOpenApi(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var options = app.Services.GetRequiredService<EidosOpenApiRouteOptions>();

        app.MapGet(options.OpenApiPath, (OpenApiDocument document) =>
        {
            using var stringWriter = new StringWriter();
            document.SerializeAsV3(new OpenApiJsonWriter(stringWriter));
            return Results.Text(stringWriter.ToString(), "application/json");
        });

        app.MapGet(options.UiPath, () =>
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

    public string UiPath { get; set; } = "/redoc";
}

public sealed class EidosOpenApiOptions
{
    public string Title { get; set; } = "Eidos API";

    public string Version { get; set; } = "0.1";

    public EidosOpenApiRouteOptions Routes { get; set; } = new();
}

public sealed class EidosMetadataOptions
{
    public string MetadataPath { get; set; } = "/routes";
}

internal sealed class EidosOpenApiSwaggerProvider(OpenApiDocument document) : ISwaggerProvider
{
    private readonly OpenApiDocument _document = document;

    public OpenApiDocument GetSwagger(string documentName, string? host = null, string? basePath = null)
    {
        return _document;
    }
}
