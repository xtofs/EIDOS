using Eidos.Core;
using Eidos.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;

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

        app.MapEidos(document, builder =>
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
        Assert.Equal(10, endpoints.Count);

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

        var exception = Assert.Throws<InvalidOperationException>(() => app.MapEidos(
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

        Assert.Throws<InvalidOperationException>(() => app.MapEidos(
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
            .Update<PersonPatchRequest>((key, request) => Results.Ok(new { key, request.Name }))
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

        app.MapEidos(document, builder =>
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

    private static WebApplication BuildApp()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        return builder.Build();
    }

    private static IResult PersonList() => Results.Ok();

    private static IResult PersonGet(string key) => Results.Ok(key);

    private static IResult PersonDelete(string key) => Results.NoContent();

    private static IResult PersonTransition(string key, Eidos.AspNetCore.StateTransitionRequest request) => Results.Ok(new { key, request.State });

    private sealed record PersonPatchRequest(string? Name);

    private static IResult PersonPost() => Results.Created("/persons/1", new { id = 1 });

    private static IResult EmploymentList() => Results.Ok();

    private static IResult EmploymentListByParticipant(string participantTypeName, string key) => Results.Ok(new { participantTypeName, key });

    private static IResult EmploymentPost() => Results.Created("/employments/1", new { id = 1 });

    private static IResult EmploymentGet(string key) => Results.Ok(key);

    private static IResult EmploymentTransition(string key, StateTransitionRequest request) => Results.Ok(new { key, request.State });

    private static IResult EmploymentDelete(string key) => Results.NoContent();

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

        app.MapEidos(document, map =>
        {
            map
                .Entity("Person", p => p
                    .List(PersonList)
                    .GetEntity(key => new Dictionary<string, object?> { ["key"] = key, ["name"] = key + "-name" })
                    .Transition(PersonTransition)
                    .Delete(PersonDelete)
                    .Create(PersonPost))
                .Relationship("Employment", e => e
                    .List(EmploymentList)
                    .GetEntity(
                        key => Results.Ok(new Dictionary<string, object?>
                        {
                            ["key"] = key,
                            ["employee"] = "person-1",
                            ["employer"] = "person-2"
                        }),
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
