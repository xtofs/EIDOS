using Eidos.AspNetCore;
using Eidos.Core;
using Microsoft.AspNetCore.JsonPatch.SystemTextJson;
using System.Reflection;

namespace Eidos.Sample.HumanResources;

/// <summary>
/// Maps the HR Eidos schema onto endpoints and implements the handlers against
/// <see cref="IHumanResourcesRepository"/>. Storage-agnostic — the repository is injected.
/// </summary>
internal sealed class HumanResourcesService(IHumanResourcesRepository repository, EidosDocumentSyntax schema)
{
    private const string SchemaResourceSuffix = ".HumanResources.HumanResourcesSchema.eidos";

    public static EidosDocumentSyntax ParsedSchema { get; } = EidosGrammarParser.Parse(LoadSchemaTextFromResource());

    private static string LoadSchemaTextFromResource()
    {
        var assembly = typeof(HumanResourcesService).Assembly;
        var resourceName = assembly
            .GetManifestResourceNames()
            .SingleOrDefault(static name => name.EndsWith(SchemaResourceSuffix, StringComparison.Ordinal));

        if (resourceName is null)
        {
            throw new InvalidOperationException($"Could not find embedded schema resource ending with '{SchemaResourceSuffix}'.");
        }

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Could not open embedded schema resource '{resourceName}'.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public void MapEndpoints(WebApplication app)
    {
        var map = app.CreateEidosMapBuilder(schema, options =>
                options.OnDiagnostic = diagnostic => app.Logger.LogDiagnostic(diagnostic))
            .Entity("Person", p => p
                .List(ListPeople)
                .GetSingle(GetPersonEntity)
                .Create<PersonCreateRequest, PersonDto>(CreatePerson)
                .Transition(TransitionPerson)
                .Update<JsonPatchDocument<PersonPatch>, PersonDto>(UpdatePerson)
                .Delete(DeletePerson))
            .Relationship("Employment", e => e
                .List(ListEmployments)
                .ListByParticipant(ListEmploymentsByParticipant)
                .GetSingle(GetEmploymentEntity, ResolvePersonForExpand, ResolvePersonForExpand)
                .Create<EmploymentCreateRequest, EmploymentDto>(CreateEmployment)
                .Transition(TransitionEmployment)
                .Update<JsonPatchDocument<EmploymentPatch>, EmploymentDto>(UpdateEmployment)
                .Delete(DeleteEmployment));

        map.MapEidosRoutes();
    }

    private async Task<Response<IReadOnlyList<PersonDto>>> ListPeople()
        => Response.Ok(await repository.ListPeopleAsync());

    private async Task<Response<PersonDto>> GetPersonEntity(string key)
    {
        var person = await repository.GetPersonAsync(key);
        return person is null ? Response.NotFound<PersonDto>() : Response.Ok(person);
    }

    private async Task<Response<PersonDto>> CreatePerson(PersonCreateRequest request)
    {
        var key = string.IsNullOrWhiteSpace(request.Key)
            ? $"person-{Guid.NewGuid():N}"[..15]
            : request.Key;

        var person = new PersonDto(key, request.Name, request.Email, "Active");

        if (!await repository.TryAddPersonAsync(person))
        {
            return Response.Conflict<PersonDto>(new { message = $"Person with key '{key}' already exists." });
        }

        return Response.Created(person, $"/persons/{key}");
    }

    private async Task<Response<PersonDto>> TransitionPerson(string key, StateTransitionRequest request)
    {
        var existing = await repository.GetPersonAsync(key);
        if (existing is null)
        {
            return Response.NotFound<PersonDto>();
        }

        var updated = existing with { State = request.State };
        await repository.SavePersonAsync(updated);
        return Response.Ok(updated);
    }

    // Apply a JSON Patch (RFC 6902) to a restricted patch model, collecting any errors (e.g. an op
    // targeting a path that isn't on the model) into a validation problem. Returns false + a 400 on error.
    private static bool TryApplyPatch<T>(JsonPatchDocument<T> patch, T target, out IResult? problem) where T : class
    {
        Dictionary<string, string[]>? errors = null;
        patch.ApplyTo(target, error =>
        {
            errors ??= new();
            var key = error.AffectedObject?.GetType().Name ?? "patch";
            var prior = errors.TryGetValue(key, out var existing) ? existing : [];
            errors[key] = [.. prior, error.ErrorMessage];
        });

        problem = errors is null ? null : TypedResults.ValidationProblem(errors);
        return errors is null;
    }

    private async Task<Response<PersonDto>> UpdatePerson(string key, JsonPatchDocument<PersonPatch> patch)
    {
        var existing = await repository.GetPersonAsync(key);
        if (existing is null)
        {
            return Response.NotFound<PersonDto>();
        }

        var model = new PersonPatch(existing.Name, existing.Email);
        if (!TryApplyPatch(patch, model, out var problem))
        {
            return Response.FromResult<PersonDto>(problem!);
        }

        var updated = existing with
        {
            Name = model.Name ?? existing.Name,
            Email = model.Email ?? existing.Email
        };

        await repository.SavePersonAsync(updated);
        return Response.Ok(updated);
    }

    private async Task<IResult> DeletePerson(string key)
        => await repository.RemovePersonAsync(key) ? Results.NoContent() : Results.NotFound();

    private async Task<Response<object>> DeletePerson2(string key)
        => await repository.RemovePersonAsync(key) ? Response.NoContent() : Response.NotFound();

    private async Task<Response<IReadOnlyList<EmploymentDto>>> ListEmployments()
        => Response.Ok(await repository.ListEmploymentsAsync());

    private async Task<Response<IReadOnlyList<EmploymentDto>>> ListEmploymentsByParticipant(string participantTypeName, string key)
    {
        if (!string.Equals(participantTypeName, "Person", StringComparison.Ordinal))
        {
            return Response.Ok<IReadOnlyList<EmploymentDto>>([]);
        }

        return Response.Ok(await repository.ListEmploymentsByParticipantAsync(key));
    }

    private async Task<Response<IDictionary<string, object?>>> GetEmploymentEntity(string key)
    {
        var employment = await repository.GetEmploymentAsync(key);
        if (employment is null)
        {
            return Response.NotFound<IDictionary<string, object?>>();
        }

        return Response.Ok<IDictionary<string, object?>>(new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["key"] = employment.Key,
            ["employee"] = employment.EmployeeKey,
            ["employer"] = employment.EmployerKey,
            ["title"] = employment.Title,
            ["state"] = employment.State
        });
    }

    private async Task<object?> ResolvePersonForExpand(string key)
        => await repository.GetPersonAsync(key);

    private async Task<Response<EmploymentDto>> CreateEmployment(EmploymentCreateRequest request)
    {
        if (!await repository.PersonExistsAsync(request.EmployeeKey)
            || !await repository.PersonExistsAsync(request.EmployerKey))
        {
            return Response.BadRequest<EmploymentDto>(new { message = "employeeKey and employerKey must reference existing persons." });
        }

        var key = string.IsNullOrWhiteSpace(request.Key)
            ? $"employment-{Guid.NewGuid():N}"[..19]
            : request.Key;

        var employment = new EmploymentDto(key, request.EmployeeKey, request.EmployerKey, request.Title, "Active");

        if (!await repository.TryAddEmploymentAsync(employment))
        {
            return Response.Conflict<EmploymentDto>(new { message = $"Employment with key '{key}' already exists." });
        }

        return Response.Created(employment, $"/employments/{key}");
    }

    private async Task<Response<EmploymentDto>> TransitionEmployment(string key, StateTransitionRequest request)
    {
        var existing = await repository.GetEmploymentAsync(key);
        if (existing is null)
        {
            return Response.NotFound<EmploymentDto>();
        }

        var updated = existing with { State = request.State };
        await repository.SaveEmploymentAsync(updated);
        return Response.Ok(updated);
    }

    private async Task<Response<EmploymentDto>> UpdateEmployment(string key, JsonPatchDocument<EmploymentPatch> patch)
    {
        var existing = await repository.GetEmploymentAsync(key);
        if (existing is null)
        {
            return Response.NotFound<EmploymentDto>();
        }

        var model = new EmploymentPatch(existing.Title);
        if (!TryApplyPatch(patch, model, out var problem))
        {
            return Response.FromResult<EmploymentDto>(problem!);
        }

        var updated = existing with { Title = model.Title ?? existing.Title };
        await repository.SaveEmploymentAsync(updated);
        return Response.Ok(updated);
    }

    private async Task<IResult> DeleteEmployment(string key)
        => await repository.RemoveEmploymentAsync(key) ? Results.NoContent() : Results.NotFound();
}
