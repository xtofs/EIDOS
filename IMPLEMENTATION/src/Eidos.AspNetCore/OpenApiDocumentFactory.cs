using System.Net.Http;
using System.Text.Json.Nodes;
using Eidos.Core.OpenApi;
using Microsoft.OpenApi;

namespace Eidos.AspNetCore;

/// <summary>
/// Transcribes the provider-neutral <see cref="OpenApiModel"/> produced by Eidos.Core into a concrete
/// <see cref="OpenApiDocument"/> (Microsoft.OpenApi). This is the only place that depends on Microsoft.OpenApi;
/// all of the §5.2 mapping logic lives in Eidos.Core. Mechanical 1:1 translation.
/// </summary>
public static class OpenApiDocumentFactory
{
    public static OpenApiDocument Create(OpenApiModel model)
    {
        var document = new OpenApiDocument
        {
            Info = new OpenApiInfo { Title = model.Info.Title, Version = model.Info.Version },
            Paths = new OpenApiPaths(),
            Components = new OpenApiComponents
            {
                Schemas = new Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal)
            }
        };

        foreach (var component in model.Components)
        {
            document.Components.Schemas[component.Name] = ToSchema(component.Schema, document);
        }

        foreach (var path in model.Paths)
        {
            var item = new OpenApiPathItem();
            foreach (var operation in path.Operations)
            {
                item.AddOperation(ToHttpMethod(operation.Method), ToOperation(operation, document));
            }

            document.Paths[path.Template] = item;
        }

        return document;
    }

    private static OpenApiOperation ToOperation(ApiOperation operation, OpenApiDocument document)
    {
        var result = new OpenApiOperation
        {
            OperationId = operation.OperationId,
            Summary = operation.Summary,
            Parameters = operation.Parameters.Count == 0
                ? null
                : operation.Parameters.Select(p => (IOpenApiParameter)ToParameter(p, document)).ToList(),
            Responses = new OpenApiResponses()
        };

        if (operation.RequestBody is { } body)
        {
            result.RequestBody = new OpenApiRequestBody
            {
                Required = body.Required,
                Content = new Dictionary<string, IOpenApiMediaType>(StringComparer.Ordinal)
                {
                    [body.MediaType] = new OpenApiMediaType { Schema = ToSchema(body.Schema, document) }
                }
            };
        }

        foreach (var response in operation.Responses)
        {
            result.Responses[response.StatusCode.ToString()] = ToResponse(response, document);
        }

        return result;
    }

    private static OpenApiParameter ToParameter(ApiParameter parameter, OpenApiDocument document) => new()
    {
        Name = parameter.Name,
        In = parameter.In == ApiParameterLocation.Path ? ParameterLocation.Path : ParameterLocation.Query,
        Required = parameter.Required,
        Description = parameter.Description,
        Schema = ToSchema(parameter.Schema, document)
    };

    private static OpenApiResponse ToResponse(ApiResponse response, OpenApiDocument document)
    {
        var result = new OpenApiResponse { Description = response.Description };
        if (response.Schema is { } schema)
        {
            result.Content = new Dictionary<string, IOpenApiMediaType>(StringComparer.Ordinal)
            {
                ["application/json"] = new OpenApiMediaType { Schema = ToSchema(schema, document) }
            };
        }

        return result;
    }

    private static IOpenApiSchema ToSchema(ApiSchema schema, OpenApiDocument document)
    {
        switch (schema)
        {
            case ApiReferenceSchema reference:
                return new OpenApiSchemaReference(reference.ComponentName, document);

            case ApiObjectSchema obj:
                {
                    var result = new OpenApiSchema
                    {
                        Type = JsonSchemaType.Object,
                        Properties = new Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal),
                        Required = new HashSet<string>(StringComparer.Ordinal)
                    };

                    foreach (var property in obj.Properties)
                    {
                        var propertySchema = ToSchema(property.Schema, document);

                        // Nullable / readOnly only apply to concrete (non-reference) schemas.
                        if (propertySchema is OpenApiSchema concrete)
                        {
                            if (property.Nullable && concrete.Type is { } t)
                            {
                                concrete.Type = t | JsonSchemaType.Null;
                            }

                            if (property.ReadOnly)
                            {
                                concrete.ReadOnly = true;
                            }
                        }

                        result.Properties[property.Name] = propertySchema;
                        if (property.Required)
                        {
                            result.Required.Add(property.Name);
                        }
                    }

                    return result;
                }

            case ApiArraySchema array:
                return new OpenApiSchema { Type = JsonSchemaType.Array, Items = ToSchema(array.Items, document) };

            case ApiEnumSchema enumeration:
                return new OpenApiSchema
                {
                    Type = JsonSchemaType.String,
                    Enum = enumeration.Values.Select(v => (JsonNode)JsonValue.Create(v)).ToList()
                };

            case ApiScalarSchema scalar:
                return new OpenApiSchema { Type = ToJsonSchemaType(scalar.Type), Format = scalar.Format };

            case ApiAnySchema:
            default:
                return new OpenApiSchema();
        }
    }

    private static JsonSchemaType ToJsonSchemaType(ApiScalarType type) => type switch
    {
        ApiScalarType.String => JsonSchemaType.String,
        ApiScalarType.Integer => JsonSchemaType.Integer,
        ApiScalarType.Number => JsonSchemaType.Number,
        ApiScalarType.Boolean => JsonSchemaType.Boolean,
        _ => JsonSchemaType.String
    };

    private static HttpMethod ToHttpMethod(ApiMethod method) => method switch
    {
        ApiMethod.Get => HttpMethod.Get,
        ApiMethod.Post => HttpMethod.Post,
        ApiMethod.Patch => HttpMethod.Patch,
        ApiMethod.Put => HttpMethod.Put,
        ApiMethod.Delete => HttpMethod.Delete,
        _ => HttpMethod.Get
    };
}
