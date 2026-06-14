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
        app.MapEidosSurface(schema, map =>
        {
            map
                .Entity("Person", p => p
                    .List(ListPeople)
                    .GetEntity(GetPersonEntity)
                    .Create<PersonCreateRequest, PersonDto>(CreatePerson)
                    .Transition(TransitionPerson)
                    .Update<JsonPatchDocument<PersonPatch>, PersonDto>(UpdatePerson)
                    .Delete(DeletePerson))
                .Relationship("Employment", e => e
                    .List(ListEmployments)
                    .ListByParticipant(ListEmploymentsByParticipant)
                    .GetEntity(GetEmploymentEntity, ResolvePersonForExpand, ResolvePersonForExpand)
                    .Create<EmploymentCreateRequest, EmploymentDto>(CreateEmployment)
                    .Transition(TransitionEmployment)
                    .Update<JsonPatchDocument<EmploymentPatch>, EmploymentDto>(UpdateEmployment)
                    .Delete(DeleteEmployment));
        }, options =>
        {
            options.OnDiagnostic = diagnostic => app.Logger.LogDiagnostic(diagnostic);
        });
    }

    private async Task<EidosResult<IReadOnlyList<PersonDto>>> ListPeople()
        => EidosResult.Ok(await repository.ListPeopleAsync());

    private async Task<EidosResult<PersonDto>> GetPersonEntity(string key)
    {
        var person = await repository.GetPersonAsync(key);
        return person is null ? EidosResult.NotFound<PersonDto>() : EidosResult.Ok(person);
    }

    private async Task<EidosResult<PersonDto>> CreatePerson(PersonCreateRequest request)
    {
        var key = string.IsNullOrWhiteSpace(request.Key)
            ? $"person-{Guid.NewGuid():N}"[..15]
            : request.Key;

        var person = new PersonDto(key, request.Name, request.Email, "Active");

        if (!await repository.TryAddPersonAsync(person))
        {
            return EidosResult.Conflict<PersonDto>(new { message = $"Person with key '{key}' already exists." });
        }

        return EidosResult.Created(person, $"/persons/{key}");
    }

    private async Task<EidosResult<PersonDto>> TransitionPerson(string key, StateTransitionRequest request)
    {
        var existing = await repository.GetPersonAsync(key);
        if (existing is null)
        {
            return EidosResult.NotFound<PersonDto>();
        }

        var updated = existing with { State = request.State };
        await repository.SavePersonAsync(updated);
        return EidosResult.Ok(updated);
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

    private async Task<EidosResult<PersonDto>> UpdatePerson(string key, JsonPatchDocument<PersonPatch> patch)
    {
        var existing = await repository.GetPersonAsync(key);
        if (existing is null)
        {
            return EidosResult.NotFound<PersonDto>();
        }

        var model = new PersonPatch(existing.Name, existing.Email);
        if (!TryApplyPatch(patch, model, out var problem))
        {
            return EidosResult.FromResult<PersonDto>(problem!);
        }

        var updated = existing with
        {
            Name = model.Name ?? existing.Name,
            Email = model.Email ?? existing.Email
        };

        await repository.SavePersonAsync(updated);
        return EidosResult.Ok(updated);
    }

    private async Task<IResult> DeletePerson(string key)
        => await repository.RemovePersonAsync(key) ? Results.NoContent() : Results.NotFound();

    private async Task<EidosResult<IReadOnlyList<EmploymentDto>>> ListEmployments()
        => EidosResult.Ok(await repository.ListEmploymentsAsync());

    private async Task<EidosResult<IReadOnlyList<EmploymentDto>>> ListEmploymentsByParticipant(string participantTypeName, string key)
    {
        if (!string.Equals(participantTypeName, "Person", StringComparison.Ordinal))
        {
            return EidosResult.Ok<IReadOnlyList<EmploymentDto>>(Array.Empty<EmploymentDto>());
        }

        return EidosResult.Ok(await repository.ListEmploymentsByParticipantAsync(key));
    }

    private async Task<EidosResult<IDictionary<string, object?>>> GetEmploymentEntity(string key)
    {
        var employment = await repository.GetEmploymentAsync(key);
        if (employment is null)
        {
            return EidosResult.NotFound<IDictionary<string, object?>>();
        }

        return EidosResult.Ok<IDictionary<string, object?>>(new Dictionary<string, object?>(StringComparer.Ordinal)
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

    private async Task<EidosResult<EmploymentDto>> CreateEmployment(EmploymentCreateRequest request)
    {
        if (!await repository.PersonExistsAsync(request.EmployeeKey)
            || !await repository.PersonExistsAsync(request.EmployerKey))
        {
            return EidosResult.BadRequest<EmploymentDto>(new { message = "employeeKey and employerKey must reference existing persons." });
        }

        var key = string.IsNullOrWhiteSpace(request.Key)
            ? $"employment-{Guid.NewGuid():N}"[..19]
            : request.Key;

        var employment = new EmploymentDto(key, request.EmployeeKey, request.EmployerKey, request.Title, "Active");

        if (!await repository.TryAddEmploymentAsync(employment))
        {
            return EidosResult.Conflict<EmploymentDto>(new { message = $"Employment with key '{key}' already exists." });
        }

        return EidosResult.Created(employment, $"/employments/{key}");
    }

    private async Task<EidosResult<EmploymentDto>> TransitionEmployment(string key, StateTransitionRequest request)
    {
        var existing = await repository.GetEmploymentAsync(key);
        if (existing is null)
        {
            return EidosResult.NotFound<EmploymentDto>();
        }

        var updated = existing with { State = request.State };
        await repository.SaveEmploymentAsync(updated);
        return EidosResult.Ok(updated);
    }

    private async Task<EidosResult<EmploymentDto>> UpdateEmployment(string key, JsonPatchDocument<EmploymentPatch> patch)
    {
        var existing = await repository.GetEmploymentAsync(key);
        if (existing is null)
        {
            return EidosResult.NotFound<EmploymentDto>();
        }

        var model = new EmploymentPatch(existing.Title);
        if (!TryApplyPatch(patch, model, out var problem))
        {
            return EidosResult.FromResult<EmploymentDto>(problem!);
        }

        var updated = existing with { Title = model.Title ?? existing.Title };
        await repository.SaveEmploymentAsync(updated);
        return EidosResult.Ok(updated);
    }

    private async Task<IResult> DeleteEmployment(string key)
        => await repository.RemoveEmploymentAsync(key) ? Results.NoContent() : Results.NotFound();
}
