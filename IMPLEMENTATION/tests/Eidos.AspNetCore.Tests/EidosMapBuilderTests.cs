using Eidos.Core;
using Eidos.Core.OpenApi;
using Eidos.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Eidos.AspNetCore.Tests;

public class EidosMapBuilderTests
{
    [Fact]
    public void MapEidos_RegistersExpectedEndpoints_WhenHandlersComplete()
    {
        var app = BuildApp();
        var document = EidosGrammarParser.Parse("""
            entity Person {
              lifecycle: Activatable
            }

            relationship Employment between
              employee : Person,
              employer : Person {

              lifecycle: Activatable
            }
            """);

        app.MapEidosHandlers(document, builder =>
        {
            builder
                .Entity("Person", p => p
                    .List(PersonList)
                    .Get(PersonGet)
                    .Delete(PersonDelete)
                    .Transition(PersonTransition)
                    .Create(PersonPost))
                .Relationship("Employment", e => e
                    .List(EmploymentList)
                    .Create(EmploymentPost)
                    .Get(EmploymentGet)
                    .Transition(EmploymentTransition)
                    .Delete(EmploymentDelete));
        });

        var endpoints = ((IEndpointRouteBuilder)app).DataSources.SelectMany(s => s.Endpoints).ToList();
        Assert.Equal(11, endpoints.Count);

        var routePatterns = endpoints
            .OfType<RouteEndpoint>()
            .Select(e => e.RoutePattern.RawText)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("/persons", routePatterns);
        Assert.Contains("/persons/{key}", routePatterns);
        Assert.Contains("/persons/{key}/_state", routePatterns);
        Assert.Contains("/employments", routePatterns);
        Assert.Contains("/employments/{key}", routePatterns);
        Assert.Contains("/employments/{key}/_state", routePatterns);
    }

    [Fact]
    public void MapEidos_Throws_WhenRequiredHandlersAreMissing()
    {
        var app = BuildApp();
        var document = EidosGrammarParser.Parse("""
            entity Person {
              lifecycle: Activatable
            }
            """);

        var diagnostics = new List<EidosRouteDiagnostic>();

        var exception = Assert.Throws<InvalidOperationException>(() => app.MapEidosHandlers(
            document,
            builder =>
            {
                builder.Entity("Person", p => p.List(PersonList));
            },
            options =>
            {
                options.FailOnError = true;
                options.OnDiagnostic = d => diagnostics.Add(d);
            }));

        Assert.Equal("Eidos route mapping has validation errors.", exception.Message);
        Assert.Contains(diagnostics, d =>
            d.ResourceType == EidosResourceType.Entity &&
            d.ResourceName == "Person" &&
            d.Operation == EidosOperationType.Get &&
            d.Severity == EidosRouteDiagnosticSeverity.Error);
    }

    [Fact]
    public void MapEidos_EmitsDiagnostics_ForUnknownDeclarationName()
    {
        var app = BuildApp();
        var document = EidosGrammarParser.Parse("""
            entity Person {
            }
            """);

        var diagnostics = new List<EidosRouteDiagnostic>();

        Assert.Throws<InvalidOperationException>(() => app.MapEidosHandlers(
            document,
            builder =>
            {
                builder.Entity("Unknown", p => p.List(PersonList));
            },
            options =>
            {
                options.FailOnError = true;
                options.OnDiagnostic = d => diagnostics.Add(d);
            }));

        Assert.Contains(diagnostics, d =>
            d.ResourceType == EidosResourceType.Entity &&
            d.ResourceName == "Unknown" &&
            d.Operation is null &&
            d.Severity == EidosRouteDiagnosticSeverity.Error);
    }

    [Fact]
    public void CreateEidosMapBuilder_AllowsFluentSnippetAndManualValidation()
    {
        var app = BuildApp();
        var document = EidosGrammarParser.Parse("""
            entity Person {
              lifecycle: Activatable
            }

            relationship Employment between
              employee : Person,
              employer : Person {

              lifecycle: Activatable
            }
            """);

        var builder = app.CreateEidosMapBuilder(document, options => options.FailOnError = true);

        builder
            .Entity("Person", p => p
                .List(PersonList)
                .Get(PersonGet)
                .Delete(PersonDelete)
                .Transition(PersonTransition)
                .Create(PersonPost))
            .Relationship("Employment", e => e
                .List(EmploymentList)
                .Create(EmploymentPost)
                .Get(EmploymentGet)
                .Transition(EmploymentTransition)
                .Delete(EmploymentDelete));

        var diagnostics = builder.ValidateCoverage();
        Assert.DoesNotContain(diagnostics, d => d.Severity == EidosRouteDiagnosticSeverity.Error);
    }

    [Fact]
    public void BuildPlainMetadataDocument_LabelsDistinctPatchOperations()
    {
        var app = BuildApp();
        var document = EidosGrammarParser.Parse("""
            entity Person {
              lifecycle: Activatable
            }
            """);

        var builder = app.CreateEidosMapBuilder(document, options => options.FailOnError = true);

        builder.Entity("Person", p => p
            .List(PersonList)
            .Get(PersonGet)
            .Create(PersonPost)
            .Transition(PersonTransition)
            .Update<PersonPatchRequest, object>((key, request) => Response.Ok<object>(new { key, request.Name }))
            .Delete(PersonDelete));

        builder.ValidateCoverage();

        var plain = builder.BuildPlainMetadataDocument();

        Assert.Contains("PUT {{baseUrl}}/persons/{key}/_state body: PutState = { \"state\": \"<TargetState>\", \"transition\"?: \"<Transition>\" }", plain, StringComparison.Ordinal);
        Assert.Contains("PATCH {{baseUrl}}/persons/{key} body: PatchProperties = [{ \"op\": \"replace\", \"path\": \"/<prop>\", \"value\": <value> }] (application/json-patch+json)", plain, StringComparison.Ordinal);
    }

    [Fact]
    public void RelationshipListByParticipant_RegistersAnchoredCollectionRoutes()
    {
        var app = BuildApp();
        var document = EidosGrammarParser.Parse("""
            relationship Employment between
              employee : Person,
              employer : Organization {
            }
            """);

        app.MapEidosHandlers(document, builder =>
        {
            builder.Relationship("Employment", e => e
                .List(EmploymentList)
                .ListByParticipant(EmploymentListByParticipant)
                .Get(EmploymentGet)
                .Create(EmploymentPost));
        });

        var routePatterns = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(s => s.Endpoints)
            .OfType<RouteEndpoint>()
            .Select(e => e.RoutePattern.RawText)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("/persons/{key}/employments", routePatterns);
        Assert.Contains("/organizations/{key}/employments", routePatterns);
    }

    private sealed record SampleDto(string Key, string Name, string State);

    [Fact]
    public async Task Responses_AreEnrichedWithReservedFields()
    {
        var app = BuildApp();
        var document = EidosGrammarParser.Parse("entity Person { lifecycle: Activatable }");

        app.CreateEidosMapBuilder(document)
            .Entity("Person", p => p.GetEntity<SampleDto>(key => Response.Ok(new SampleDto(key, "Ada", "Active"))));

        await app.StartAsync();
        try
        {
            var json = await app.GetTestClient().GetStringAsync("/persons/ada");
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            Assert.Equal("Person", doc.RootElement.GetProperty("_type").GetString());
            Assert.Equal("/persons/ada", doc.RootElement.GetProperty("_self").GetString());
            Assert.Equal("Active", doc.RootElement.GetProperty("_state").GetString());
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    private static WebApplication BuildApp()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.Logging.SetMinimumLevel(LogLevel.None);
        builder.WebHost.UseTestServer();
        return builder.Build();
    }

    private static Response<object> PersonList() => Response.Ok<object>(Array.Empty<object>());

    private static Response<object> PersonGet(string key) => Response.Ok<object>(new { key });

    private static IResult PersonDelete(string key) => Results.NoContent();

    private static Response<object> PersonTransition(string key, StateTransitionRequest request) => Response.Ok<object>(new { key, request.State });

    private sealed record PersonPatchRequest(string? Name);

    private static Response<object> PersonPost() => Response.Created<object>(new { id = 1 }, "/persons/1");

    private static Response<object> EmploymentList() => Response.Ok<object>(Array.Empty<object>());

    private static Response<object> EmploymentListByParticipant(string participantTypeName, string key) => Response.Ok<object>(new { participantTypeName, key });

    private static Response<object> EmploymentPost() => Response.Created<object>(new { id = 1 }, "/employments/1");

    private static Response<object> EmploymentGet(string key) => Response.Ok<object>(new { key });

    private static Response<object> EmploymentTransition(string key, StateTransitionRequest request) => Response.Ok<object>(new { key, request.State });

    private static IResult EmploymentDelete(string key) => Results.NoContent();

    [Fact]
    public async Task MapOpenApiEndpoint_ServesValidOpenApiJson()
    {
        var app = BuildApp();
        var document = EidosGrammarParser.Parse("""
            entity Person {
              lifecycle: Activatable
            }
            """);

        app.CreateEidosMapBuilder(document).MapOpenApiEndpoint("/openapi.json", new ApiInfo("Test API", "1.0"));

        await app.StartAsync();
        try
        {
            var response = await app.GetTestClient().GetAsync("/openapi.json");
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync();

            using var parsed = System.Text.Json.JsonDocument.Parse(json); // must be valid JSON
            Assert.StartsWith("3.0", parsed.RootElement.GetProperty("openapi").GetString());
            Assert.Contains("/persons", json, StringComparison.Ordinal);
            Assert.Contains("/persons/{key}/_state", json, StringComparison.Ordinal);
            Assert.True(parsed.RootElement.GetProperty("components").GetProperty("schemas").TryGetProperty("Person", out _));
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task AddEidosOpenApi_ResolvesSchemaFromDi()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.Logging.SetMinimumLevel(LogLevel.None);
        builder.WebHost.UseTestServer();

        var document = EidosGrammarParser.Parse("""
            entity Person {
              lifecycle: Activatable
            }
            """);

        builder.Services.AddSingleton(document);
        builder.AddEidosOpenApi("Test API", "1.0", options => options.OpenApiPath = "/openapi.json");

        var app = builder.Build();
        app.MapEidosOpenApi();

        await app.StartAsync();
        try
        {
            var response = await app.GetTestClient().GetAsync("/openapi.json");
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync();

            using var parsed = System.Text.Json.JsonDocument.Parse(json);
            Assert.StartsWith("3.0", parsed.RootElement.GetProperty("openapi").GetString());
            Assert.Contains("/persons", json, StringComparison.Ordinal);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task MapEidosHandlers_MapsMetadataEndpoint_WhenOpenApiRouteOptionsRegistered()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.Logging.SetMinimumLevel(LogLevel.None);
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(new EidosMetadataOptions { MetadataPath = "/routes" });

        var app = builder.Build();
        var document = EidosGrammarParser.Parse("""
            entity Person {
              lifecycle: Activatable
            }
            """);

        app.MapEidosHandlers(document, map =>
        {
            map.Entity("Person", p => p
                .List(PersonList)
                .Get(PersonGet)
                .Create(PersonPost)
                .Transition(PersonTransition)
                .Delete(PersonDelete));
        });

        await app.StartAsync();
        try
        {
            var response = await app.GetTestClient().GetAsync("/routes");
            response.EnsureSuccessStatusCode();
            var payload = await response.Content.ReadAsStringAsync();
            Assert.Contains("/persons", payload, StringComparison.Ordinal);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task MapEidosSurface_MapsHandlersAndConfiguredOpenApiRoutes()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.Logging.SetMinimumLevel(LogLevel.None);
        builder.WebHost.UseTestServer();

        var document = EidosGrammarParser.Parse("""
            entity Person {
              lifecycle: Activatable
            }
            """);

        builder.Services.AddSingleton(document);
        builder.AddEidosOpenApi("Test API", "1.0", options =>
        {
            options.OpenApiPath = "/openapi.json";
            options.UiPath = "/docs";
        });
        builder.AddEidosMetadata(new EidosMetadataOptions { MetadataPath = "/routes" });

        var app = builder.Build();

        app.MapEidosSurface(document, map =>
        {
            map.Entity("Person", p => p
                .List(PersonList)
                .Get(PersonGet)
                .Create(PersonPost)
                .Transition(PersonTransition)
                .Delete(PersonDelete));
        });

        await app.StartAsync();
        try
        {
            var client = app.GetTestClient();

            var openApiResponse = await client.GetAsync("/openapi.json");
            openApiResponse.EnsureSuccessStatusCode();

            var docsResponse = await client.GetAsync("/docs");
            docsResponse.EnsureSuccessStatusCode();

            var metadataResponse = await client.GetAsync("/routes");
            metadataResponse.EnsureSuccessStatusCode();

            var metadataPayload = await metadataResponse.Content.ReadAsStringAsync();
            Assert.Contains("/persons", metadataPayload, StringComparison.Ordinal);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task GetRelationship_ExpandsParticipants_WhenExpandQueryIsProvided()
    {
        var app = BuildApp();
        var document = EidosGrammarParser.Parse("""
            entity Person {
              lifecycle: Activatable
            }

            relationship Employment between
              employee : Person,
              employer : Person {

              lifecycle: Activatable
            }
            """);

        app.MapEidosHandlers(document, map =>
        {
            map
                .Entity("Person", p => p
                    .List(PersonList)
                    .GetEntity<IDictionary<string, object?>>(key => Response.Ok<IDictionary<string, object?>>(new Dictionary<string, object?> { ["key"] = key, ["name"] = key + "-name" }))
                    .Transition(PersonTransition)
                    .Delete(PersonDelete)
                    .Create(PersonPost))
                .Relationship("Employment", e => e
                    .List(EmploymentList)
                    .GetEntity(
                        key => Task.FromResult(Response.Ok<IDictionary<string, object?>>(new Dictionary<string, object?>
                        {
                            ["key"] = key,
                            ["employee"] = "person-1",
                            ["employer"] = "person-2"
                        })),
                        key => Task.FromResult<object?>(new Dictionary<string, object?> { ["key"] = key, ["name"] = key + "-name" }),
                        key => Task.FromResult<object?>(new Dictionary<string, object?> { ["key"] = key, ["name"] = key + "-name" }))
                    .Transition(EmploymentTransition)
                    .Delete(EmploymentDelete)
                    .Create(EmploymentPost));
        });

        await app.StartAsync();
        try
        {
            var client = app.GetTestClient();
            var response = await client.GetAsync("/employments/employment-1?expand=employee,employer");
            response.EnsureSuccessStatusCode();
            var payload = await response.Content.ReadAsStringAsync();
            Assert.Contains("employee", payload, StringComparison.Ordinal);
            Assert.Contains("person-1-name", payload, StringComparison.Ordinal);
            Assert.Contains("person-2-name", payload, StringComparison.Ordinal);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }
}
