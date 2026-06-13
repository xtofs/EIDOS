using Eidos.Parser;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using System.Globalization;

namespace Eidos.AspNetCore;

public sealed class EidosMapBuilder
{
    private readonly IEndpointRouteBuilder _endpoints;
    private readonly EidosRouteMappingOptions _options;
    private readonly IEidosOperationPolicy _operationPolicy;

    private readonly Dictionary<string, EntityDeclarationSyntax> _entities;
    private readonly Dictionary<string, RelationshipDeclarationSyntax> _relationships;

    private readonly Dictionary<(EidosResourceType Type, string Name), HashSet<EidosOperationType>> _registrations =
        new();
    private readonly List<EidosRouteDiagnostic> _preValidationDiagnostics = [];
    private readonly List<EidosMappedRoute> _mappedRoutes = [];

    private readonly Dictionary<string, Func<string, object?>> _entityResolvers =
        new(StringComparer.Ordinal);

    internal EidosMapBuilder(
        IEndpointRouteBuilder endpoints,
        EidosDocumentSyntax document,
        EidosRouteMappingOptions options,
        IEidosOperationPolicy operationPolicy)
    {
        _endpoints = endpoints;
        _options = options;
        _operationPolicy = operationPolicy;

        _entities = document.Declarations
            .OfType<EntityDeclarationSyntax>()
            .ToDictionary(d => d.Name, StringComparer.Ordinal);

        _relationships = document.Declarations
            .OfType<RelationshipDeclarationSyntax>()
            .ToDictionary(d => d.Name, StringComparer.Ordinal);
    }

    public EidosMapBuilder Entity(string name, Action<EidosEntityRouteBuilder> configure)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(configure);

        if (!_entities.TryGetValue(name, out var declaration))
        {
            ReportImmediateDiagnostic(new EidosRouteDiagnostic(
                EidosRouteDiagnosticSeverity.Error,
                $"Entity '{name}' is not declared in the Eidos document.",
                EidosResourceType.Entity,
                name,
                null,
                null));
            return this;
        }

        var builder = new EidosEntityRouteBuilder(
            _endpoints,
            declaration,
            _options,
            Register,
            RegisterMappedRoute,
            RegisterEntityResolver);
        configure(builder);

        return this;
    }

    public EidosMapBuilder Relationship(string name, Action<EidosRelationshipRouteBuilder> configure)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(configure);

        if (!_relationships.TryGetValue(name, out var declaration))
        {
            ReportImmediateDiagnostic(new EidosRouteDiagnostic(
                EidosRouteDiagnosticSeverity.Error,
                $"Relationship '{name}' is not declared in the Eidos document.",
                EidosResourceType.Relationship,
                name,
                null,
                null));
            return this;
        }

        var builder = new EidosRelationshipRouteBuilder(
            _endpoints,
            declaration,
            _options,
            Register,
            RegisterMappedRoute,
            TryResolveEntity,
            ReportImmediateDiagnostic);
        configure(builder);

        return this;
    }

    public IReadOnlyList<EidosRouteDiagnostic> ValidateCoverage()
    {
        var diagnostics = new List<EidosRouteDiagnostic>();

        foreach (var entity in _entities.Values)
        {
            ValidateResourceCoverage(
                EidosResourceType.Entity,
                entity.Name,
                entity.Span,
                _operationPolicy.RequiredForEntity(entity),
                diagnostics);
        }

        foreach (var relationship in _relationships.Values)
        {
            ValidateResourceCoverage(
                EidosResourceType.Relationship,
                relationship.Name,
                relationship.Span,
                _operationPolicy.RequiredForRelationship(relationship),
                diagnostics);
        }

        foreach (var diagnostic in diagnostics)
        {
            EmitDiagnostic(diagnostic);
        }

        diagnostics.InsertRange(0, _preValidationDiagnostics);

        if (_options.FailOnError && diagnostics.Any(d => d.Severity == EidosRouteDiagnosticSeverity.Error))
        {
            throw new InvalidOperationException("Eidos route mapping has validation errors.");
        }

        return diagnostics;
    }

    public EidosMapBuilder MapMetadataEndpoint(string pattern = "/")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);
        _endpoints.MapGet(pattern, GetMetadata);
        return this;
    }
    // Task RequestDelegate(HttpContext context);
    async Task<IResult> GetMetadata(HttpRequest request)
    {
        return request.Query.TryGetValue("format", out var format) && string.Equals(format, "plain", StringComparison.OrdinalIgnoreCase)
            ? Results.Text(BuildPlainMetadataDocument())
            : Results.Ok(BuildMetadataDocument());
    }

    public EidosRouteMetadataDocument BuildMetadataDocument()
    {
        var routes = _mappedRoutes
            .OrderBy(r => r.Path, StringComparer.Ordinal)
            .ThenBy(r => r.ResourceType)
            .ThenBy(r => r.ResourceName, StringComparer.Ordinal)
            .ThenBy(r => r.Operation)
            .ToArray();

        return new EidosRouteMetadataDocument(routes);
    }

    public string BuildPlainMetadataDocument()
    {
        var routes = _mappedRoutes
            .OrderBy(r => r.Path, StringComparer.Ordinal)
            .ThenBy(r => r.ResourceType)
            .ThenBy(r => r.ResourceName, StringComparer.Ordinal)
            .ThenBy(r => r.Operation)
            .ToArray();

        return string
            .Join("\n\n", routes
            .SelectMany(r => r.Methods
            .Select(m => $"### {r.ResourceType} {r.ResourceName}\n{m} {{{{baseUrl}}}}{r.Path} body: {r.Operation} = {BuildOperationHint(r.Operation)}")));
    }

    private static string BuildOperationHint(EidosOperationType operation)
    {
        return operation switch
        {
            EidosOperationType.PutState => "{ \"state\": \"<TargetState>\", \"transition\"?: \"<Transition>\" }",
            EidosOperationType.PatchProperties => "[{ \"op\": \"replace\", \"path\": \"/<prop>\", \"value\": <value> }] (application/json-patch+json)",
            _ => string.Empty
        };
    }

    private void RegisterEntityResolver(string kindName, Func<string, object?> resolver)
    {
        _entityResolvers[kindName] = resolver;
    }

    private bool TryResolveEntity(string kindName, string key, out object? entity)
    {
        if (_entityResolvers.TryGetValue(kindName, out var resolver))
        {
            entity = resolver(key);
            return entity is not null;
        }

        entity = null;
        return false;
    }

    private void ValidateResourceCoverage(
        EidosResourceType resourceType,
        string resourceName,
        SourceSpan span,
        IReadOnlySet<EidosOperationType> required,
        List<EidosRouteDiagnostic> diagnostics)
    {
        var key = (resourceType, resourceName);
        _registrations.TryGetValue(key, out var registered);
        registered ??= [];

        foreach (var operation in required)
        {
            if (!registered.Contains(operation))
            {
                diagnostics.Add(new EidosRouteDiagnostic(
                    EidosRouteDiagnosticSeverity.Error,
                    $"Missing handler for {resourceType} '{resourceName}', operation '{operation}'.",
                    resourceType,
                    resourceName,
                    operation,
                    span));
            }
        }

        foreach (var operation in registered)
        {
            if (!required.Contains(operation))
            {
                diagnostics.Add(new EidosRouteDiagnostic(
                    EidosRouteDiagnosticSeverity.Warning,
                    $"Operation '{operation}' for {resourceType} '{resourceName}' is registered but not required by the current policy.",
                    resourceType,
                    resourceName,
                    operation,
                    span));
            }
        }
    }

    private void Register(EidosResourceType resourceType, string name, EidosOperationType operation)
    {
        var key = (resourceType, name);
        if (!_registrations.TryGetValue(key, out var operations))
        {
            operations = [];
            _registrations[key] = operations;
        }

        operations.Add(operation);
    }

    private void RegisterMappedRoute(
        EidosResourceType resourceType,
        string resourceName,
        EidosOperationType operation,
        string path,
        IReadOnlyList<string> methods)
    {
        var normalizedMethods = methods
            .Select(m => m.ToUpperInvariant())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(m => m, StringComparer.Ordinal)
            .ToArray();

        if (_mappedRoutes.Any(r =>
                r.ResourceType == resourceType &&
                string.Equals(r.ResourceName, resourceName, StringComparison.Ordinal) &&
                r.Operation == operation &&
                string.Equals(r.Path, path, StringComparison.Ordinal) &&
                r.Methods.SequenceEqual(normalizedMethods, StringComparer.Ordinal)))
        {
            return;
        }

        _mappedRoutes.Add(new EidosMappedRoute(resourceType, resourceName, operation, path, normalizedMethods));
    }

    private void EmitDiagnostic(EidosRouteDiagnostic diagnostic)
    {
        _options.OnDiagnostic?.Invoke(diagnostic);
    }

    private void ReportImmediateDiagnostic(EidosRouteDiagnostic diagnostic)
    {
        _preValidationDiagnostics.Add(diagnostic);
        EmitDiagnostic(diagnostic);
    }
}

public sealed record EidosRouteMetadataDocument(IReadOnlyList<EidosMappedRoute> Routes);

public sealed record EidosMappedRoute(
    EidosResourceType ResourceType,
    string ResourceName,
    EidosOperationType Operation,
    string Path,
    IReadOnlyList<string> Methods);

public sealed class EidosEntityRouteBuilder
{
    // Property updates use JSON Patch (RFC 6902); state changes go through PUT /_state.
    internal const string JsonPatchMediaType = "application/json-patch+json";

    private readonly IEndpointRouteBuilder _endpoints;
    private readonly EntityDeclarationSyntax _declaration;
    private readonly EidosRouteMappingOptions _options;
    private readonly Action<EidosResourceType, string, EidosOperationType> _register;
    private readonly Action<EidosResourceType, string, EidosOperationType, string, IReadOnlyList<string>> _registerMappedRoute;
    private readonly Action<string, Func<string, object?>> _registerEntityResolver;

    internal EidosEntityRouteBuilder(
        IEndpointRouteBuilder endpoints,
        EntityDeclarationSyntax declaration,
        EidosRouteMappingOptions options,
        Action<EidosResourceType, string, EidosOperationType> register,
        Action<EidosResourceType, string, EidosOperationType, string, IReadOnlyList<string>> registerMappedRoute,
        Action<string, Func<string, object?>> registerEntityResolver)
    {
        _endpoints = endpoints;
        _declaration = declaration;
        _options = options;
        _register = register;
        _registerMappedRoute = registerMappedRoute;
        _registerEntityResolver = registerEntityResolver;
    }

    public EidosEntityRouteBuilder List(Func<IResult> handler)
    {
        var path = CollectionPath();
        _endpoints.MapGet(path, handler);
        Register(EidosOperationType.List);
        RegisterMappedRoute(EidosOperationType.List, path, "GET");
        return this;
    }

    public EidosEntityRouteBuilder List(Func<Task<IResult>> handler)
    {
        var path = CollectionPath();
        _endpoints.MapGet(path, handler);
        Register(EidosOperationType.List);
        RegisterMappedRoute(EidosOperationType.List, path, "GET");
        return this;
    }

    public EidosEntityRouteBuilder Create<TRequest>(Func<TRequest, IResult> handler)
    {
        var path = CollectionPath();
        _endpoints.MapPost(path, handler);
        Register(EidosOperationType.Post);
        RegisterMappedRoute(EidosOperationType.Post, path, "POST");
        return this;
    }

    public EidosEntityRouteBuilder Create<TRequest>(Func<TRequest, Task<IResult>> handler)
    {
        var path = CollectionPath();
        _endpoints.MapPost(path, handler);
        Register(EidosOperationType.Post);
        RegisterMappedRoute(EidosOperationType.Post, path, "POST");
        return this;
    }

    public EidosEntityRouteBuilder Create(Func<IResult> handler)
    {
        var path = CollectionPath();
        _endpoints.MapPost(path, handler);
        Register(EidosOperationType.Post);
        RegisterMappedRoute(EidosOperationType.Post, path, "POST");
        return this;
    }

    public EidosEntityRouteBuilder Create(Func<Task<IResult>> handler)
    {
        var path = CollectionPath();
        _endpoints.MapPost(path, handler);
        Register(EidosOperationType.Post);
        RegisterMappedRoute(EidosOperationType.Post, path, "POST");
        return this;
    }

    public EidosEntityRouteBuilder Post<TRequest>(Func<TRequest, IResult> handler) => Create(handler);

    public EidosEntityRouteBuilder Post<TRequest>(Func<TRequest, Task<IResult>> handler) => Create(handler);

    public EidosEntityRouteBuilder Post(Func<IResult> handler) => Create(handler);

    public EidosEntityRouteBuilder Post(Func<Task<IResult>> handler) => Create(handler);

    public EidosEntityRouteBuilder Get(Func<string, IResult> handler)
    {
        var path = ItemPath();
        _endpoints.MapGet(path, handler);
        Register(EidosOperationType.Get);
        RegisterMappedRoute(EidosOperationType.Get, path, "GET");
        return this;
    }

    public EidosEntityRouteBuilder Get(Func<string, Task<IResult>> handler)
    {
        var path = ItemPath();
        _endpoints.MapGet(path, handler);
        Register(EidosOperationType.Get);
        RegisterMappedRoute(EidosOperationType.Get, path, "GET");
        return this;
    }

    public EidosEntityRouteBuilder Get(Func<string, HttpRequest, IResult> handler)
    {
        var path = ItemPath();
        _endpoints.MapGet(path, handler);
        Register(EidosOperationType.Get);
        RegisterMappedRoute(EidosOperationType.Get, path, "GET");
        return this;
    }

    public EidosEntityRouteBuilder Get(Func<string, HttpRequest, Task<IResult>> handler)
    {
        var path = ItemPath();
        _endpoints.MapGet(path, handler);
        Register(EidosOperationType.Get);
        RegisterMappedRoute(EidosOperationType.Get, path, "GET");
        return this;
    }

    public EidosEntityRouteBuilder GetEntity(Func<string, object?> handler)
    {
        _registerEntityResolver(_declaration.Name, handler);
        var path = ItemPath();
        _endpoints.MapGet(path, (string key) =>
        {
            var entity = handler(key);
            return entity is null ? Results.NotFound() : Results.Ok(entity);
        });
        Register(EidosOperationType.Get);
        RegisterMappedRoute(EidosOperationType.Get, path, "GET");
        return this;
    }

    public EidosEntityRouteBuilder GetEntity(Func<string, IResult> handler)
    {
        _registerEntityResolver(_declaration.Name, key => ExtractEntityFromResult(handler(key)));
        var path = ItemPath();
        _endpoints.MapGet(path, handler);
        Register(EidosOperationType.Get);
        RegisterMappedRoute(EidosOperationType.Get, path, "GET");
        return this;
    }

    public EidosEntityRouteBuilder GetEntity(Func<string, Task<IResult>> handler)
    {
        _registerEntityResolver(_declaration.Name, key => ExtractEntityFromResult(handler(key).GetAwaiter().GetResult()));
        var path = ItemPath();
        _endpoints.MapGet(path, handler);
        Register(EidosOperationType.Get);
        RegisterMappedRoute(EidosOperationType.Get, path, "GET");
        return this;
    }

    public EidosEntityRouteBuilder GetEntity(Func<string, Task<object?>> handler)
    {
        _registerEntityResolver(_declaration.Name, key => handler(key).GetAwaiter().GetResult());
        var path = ItemPath();
        _endpoints.MapGet(path, async (string key) =>
        {
            var entity = await handler(key).ConfigureAwait(false);
            return entity is null ? Results.NotFound() : Results.Ok(entity);
        });
        Register(EidosOperationType.Get);
        RegisterMappedRoute(EidosOperationType.Get, path, "GET");
        return this;
    }

    public EidosEntityRouteBuilder Transition(Func<string, StateTransitionRequest, IResult> handler)
    {
        var path = StatePath();
        _endpoints.MapMethods(path, ["PUT"], handler);
        Register(EidosOperationType.PutState);
        RegisterMappedRoute(EidosOperationType.PutState, path, "PUT");
        return this;
    }

    public EidosEntityRouteBuilder Transition(Func<string, StateTransitionRequest, Task<IResult>> handler)
    {
        var path = StatePath();
        _endpoints.MapMethods(path, ["PUT"], handler);
        Register(EidosOperationType.PutState);
        RegisterMappedRoute(EidosOperationType.PutState, path, "PUT");
        return this;
    }

    public EidosEntityRouteBuilder Update<TRequest>(Func<string, TRequest, IResult> handler)
        where TRequest : notnull
    {
        var path = ItemPath();
        _endpoints.MapMethods(path, ["PATCH"], handler)
            .Accepts<TRequest>(JsonPatchMediaType);
        Register(EidosOperationType.PatchProperties);
        RegisterMappedRoute(EidosOperationType.PatchProperties, path, "PATCH");
        return this;
    }

    public EidosEntityRouteBuilder Update<TRequest>(Func<string, TRequest, Task<IResult>> handler)
        where TRequest : notnull
    {
        var path = ItemPath();
        _endpoints.MapMethods(path, ["PATCH"], handler)
            .Accepts<TRequest>(JsonPatchMediaType);
        Register(EidosOperationType.PatchProperties);
        RegisterMappedRoute(EidosOperationType.PatchProperties, path, "PATCH");
        return this;
    }

    public EidosEntityRouteBuilder PatchState(Func<string, StateTransitionRequest, IResult> handler) => Transition(handler);

    public EidosEntityRouteBuilder PatchState(Func<string, StateTransitionRequest, Task<IResult>> handler) => Transition(handler);

    public EidosEntityRouteBuilder PatchProperties<TRequest>(Func<string, TRequest, IResult> handler) where TRequest : notnull => Update(handler);

    public EidosEntityRouteBuilder PatchProperties<TRequest>(Func<string, TRequest, Task<IResult>> handler) where TRequest : notnull => Update(handler);

    public EidosEntityRouteBuilder Delete(Func<string, IResult> handler)
    {
        var path = ItemPath();
        _endpoints.MapDelete(path, handler);
        Register(EidosOperationType.Delete);
        RegisterMappedRoute(EidosOperationType.Delete, path, "DELETE");
        return this;
    }

    public EidosEntityRouteBuilder Delete(Func<string, Task<IResult>> handler)
    {
        var path = ItemPath();
        _endpoints.MapDelete(path, handler);
        Register(EidosOperationType.Delete);
        RegisterMappedRoute(EidosOperationType.Delete, path, "DELETE");
        return this;
    }

    public EidosEntityRouteBuilder WithCollectionPath(string path)
    {
        _customCollectionPath = path;
        return this;
    }

    public EidosEntityRouteBuilder WithItemPath(string path)
    {
        _customItemPath = path;
        return this;
    }

    private string? _customCollectionPath;
    private string? _customItemPath;

    private string CollectionPath()
    {
        return _customCollectionPath ?? '/' + _options.CollectionSegmentStrategy(_declaration.Name);
    }

    private string ItemPath()
    {
        return _customItemPath ?? $"{CollectionPath()}/{{{_options.ItemRouteParameterStrategy(_declaration.Name)}}}";
    }

    private string StatePath()
    {
        return _customItemPath is null
            ? $"{ItemPath()}/_state"
            : $"{_customItemPath}/_state";
    }

    private void Register(EidosOperationType operation)
    {
        _register(EidosResourceType.Entity, _declaration.Name, operation);
    }

    private void RegisterMappedRoute(EidosOperationType operation, string path, params string[] methods)
    {
        _registerMappedRoute(EidosResourceType.Entity, _declaration.Name, operation, path, methods);
    }

    private static object? ExtractEntityFromResult(IResult result)
    {
        if (result is IValueHttpResult valueResult)
        {
            return valueResult.Value;
        }

        return null;
    }
}

public sealed class EidosRelationshipRouteBuilder
{
    private readonly EidosEntityRouteBuilder _inner;
    private readonly IEndpointRouteBuilder _endpoints;
    private readonly EidosRouteMappingOptions _options;
    private readonly RelationshipDeclarationSyntax _declaration;
    private readonly TryResolveEntityDelegate _resolveEntity;
    private readonly Action<EidosRouteDiagnostic> _reportDiagnostic;
    private readonly Action<EidosResourceType, string, EidosOperationType> _register;
    private readonly Action<EidosResourceType, string, EidosOperationType, string, IReadOnlyList<string>> _registerMappedRoute;

    internal delegate bool TryResolveEntityDelegate(string kindName, string key, out object? entity);

    internal EidosRelationshipRouteBuilder(
        IEndpointRouteBuilder endpoints,
        RelationshipDeclarationSyntax declaration,
        EidosRouteMappingOptions options,
        Action<EidosResourceType, string, EidosOperationType> register,
        Action<EidosResourceType, string, EidosOperationType, string, IReadOnlyList<string>> registerMappedRoute,
        TryResolveEntityDelegate resolveEntity,
        Action<EidosRouteDiagnostic> reportDiagnostic)
    {
        _endpoints = endpoints;
        _options = options;
        _declaration = declaration;
        _resolveEntity = resolveEntity;
        _reportDiagnostic = reportDiagnostic;
        _register = register;
        _registerMappedRoute = registerMappedRoute;

        _inner = new EidosEntityRouteBuilder(
            endpoints,
            new EntityDeclarationSyntax(declaration.Name, [], [], declaration.Annotations, declaration.Span),
            options,
            (_, _, operation) => register(EidosResourceType.Relationship, declaration.Name, operation),
            (_, _, operation, path, methods) => registerMappedRoute(EidosResourceType.Relationship, declaration.Name, operation, path, methods),
            (_, _) => { });
    }

    public EidosRelationshipRouteBuilder List(Func<IResult> handler)
    {
        _inner.List(handler);
        return this;
    }

    public EidosRelationshipRouteBuilder List(Func<Task<IResult>> handler)
    {
        _inner.List(handler);
        return this;
    }

    public EidosRelationshipRouteBuilder ListByParticipant(Func<string, string, IResult> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        foreach (var anchored in BuildAnchoredCollectionPaths())
        {
            _endpoints.MapGet(anchored.Path, (HttpRequest request) =>
            {
                if (!TryGetRouteValue(request, anchored.RouteParameterName, out var key))
                {
                    return Results.BadRequest(new
                    {
                        message = $"Missing route value '{anchored.RouteParameterName}'."
                    });
                }

                return handler(anchored.ParticipantTypeName, key);
            });
            RegisterAnchoredListRoute(anchored.Path);
        }

        return this;
    }

    public EidosRelationshipRouteBuilder ListByParticipant(Func<string, string, Task<IResult>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        foreach (var anchored in BuildAnchoredCollectionPaths())
        {
            _endpoints.MapGet(anchored.Path, async (HttpRequest request) =>
            {
                if (!TryGetRouteValue(request, anchored.RouteParameterName, out var key))
                {
                    return Results.BadRequest(new
                    {
                        message = $"Missing route value '{anchored.RouteParameterName}'."
                    });
                }

                return await handler(anchored.ParticipantTypeName, key).ConfigureAwait(false);
            });
            RegisterAnchoredListRoute(anchored.Path);
        }

        return this;
    }

    public EidosRelationshipRouteBuilder Create<TRequest>(Func<TRequest, IResult> handler)
    {
        _inner.Create(handler);
        return this;
    }

    public EidosRelationshipRouteBuilder Create<TRequest>(Func<TRequest, Task<IResult>> handler)
    {
        _inner.Create(handler);
        return this;
    }

    public EidosRelationshipRouteBuilder Create(Func<IResult> handler)
    {
        _inner.Create(handler);
        return this;
    }

    public EidosRelationshipRouteBuilder Create(Func<Task<IResult>> handler)
    {
        _inner.Create(handler);
        return this;
    }

    public EidosRelationshipRouteBuilder Post<TRequest>(Func<TRequest, IResult> handler) => Create(handler);

    public EidosRelationshipRouteBuilder Post<TRequest>(Func<TRequest, Task<IResult>> handler) => Create(handler);

    public EidosRelationshipRouteBuilder Post(Func<IResult> handler) => Create(handler);

    public EidosRelationshipRouteBuilder Post(Func<Task<IResult>> handler) => Create(handler);

    public EidosRelationshipRouteBuilder Get(Func<string, IResult> handler)
    {
        _inner.Get(handler);
        return this;
    }

    public EidosRelationshipRouteBuilder Get(Func<string, Task<IResult>> handler)
    {
        _inner.Get(handler);
        return this;
    }

    public EidosRelationshipRouteBuilder GetEntity(Func<string, IDictionary<string, object?>?> handler)
    {
        _inner.Get(async (string key, HttpRequest request) =>
        {
            var baseEntity = handler(key);
            if (baseEntity is null)
            {
                return Results.NotFound();
            }

            return await BuildExpandedResultAsync(baseEntity, request).ConfigureAwait(false);
        });
        return this;
    }

    public EidosRelationshipRouteBuilder GetEntity(Func<string, Task<IDictionary<string, object?>?>> handler)
    {
        _inner.Get(async (string key, HttpRequest request) =>
        {
            var baseEntity = await handler(key).ConfigureAwait(false);
            if (baseEntity is null)
            {
                return Results.NotFound();
            }

            return await BuildExpandedResultAsync(baseEntity, request).ConfigureAwait(false);
        });
        return this;
    }

    public EidosRelationshipRouteBuilder GetEntity(Func<string, IResult> handler)
    {
        _inner.Get(async (string key, HttpRequest request) =>
        {
            var result = handler(key);
            if (!TryExtractEntityMap(result, out var baseEntity))
            {
                return result;
            }

            return await BuildExpandedResultAsync(baseEntity, request).ConfigureAwait(false);
        });
        return this;
    }

    public EidosRelationshipRouteBuilder GetEntity(
        Func<string, IResult> handler,
        Func<string, Task<object?>> firstParticipantEntityResolver,
        Func<string, Task<object?>> secondParticipantEntityResolver)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(firstParticipantEntityResolver);
        ArgumentNullException.ThrowIfNull(secondParticipantEntityResolver);

        _inner.Get(async (string key, HttpRequest request) =>
        {
            var result = handler(key);
            if (!TryExtractEntityMap(result, out var baseEntity))
            {
                return result;
            }

            var participantResolvers = BuildTwoParticipantResolvers(firstParticipantEntityResolver, secondParticipantEntityResolver);
            if (participantResolvers is null)
            {
                return result;
            }

            return await BuildExpandedResultAsync(baseEntity, request, participantResolvers).ConfigureAwait(false);
        });
        return this;
    }

    public EidosRelationshipRouteBuilder GetEntity(Func<string, Task<IResult>> handler)
    {
        _inner.Get(async (string key, HttpRequest request) =>
        {
            var result = await handler(key).ConfigureAwait(false);
            if (!TryExtractEntityMap(result, out var baseEntity))
            {
                return result;
            }

            return await BuildExpandedResultAsync(baseEntity, request).ConfigureAwait(false);
        });
        return this;
    }

    public EidosRelationshipRouteBuilder GetEntity(
        Func<string, Task<IResult>> handler,
        Func<string, Task<object?>> firstParticipantEntityResolver,
        Func<string, Task<object?>> secondParticipantEntityResolver)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(firstParticipantEntityResolver);
        ArgumentNullException.ThrowIfNull(secondParticipantEntityResolver);

        _inner.Get(async (string key, HttpRequest request) =>
        {
            var result = await handler(key).ConfigureAwait(false);
            if (!TryExtractEntityMap(result, out var baseEntity))
            {
                return result;
            }

            var participantResolvers = BuildTwoParticipantResolvers(firstParticipantEntityResolver, secondParticipantEntityResolver);
            if (participantResolvers is null)
            {
                return result;
            }

            return await BuildExpandedResultAsync(baseEntity, request, participantResolvers).ConfigureAwait(false);
        });
        return this;
    }

    public EidosRelationshipRouteBuilder Transition(Func<string, StateTransitionRequest, IResult> handler)
    {
        _inner.Transition(handler);
        return this;
    }

    public EidosRelationshipRouteBuilder Transition(Func<string, StateTransitionRequest, Task<IResult>> handler)
    {
        _inner.Transition(handler);
        return this;
    }

    public EidosRelationshipRouteBuilder Update<TRequest>(Func<string, TRequest, IResult> handler)
        where TRequest : notnull
    {
        _inner.Update(handler);
        return this;
    }

    public EidosRelationshipRouteBuilder Update<TRequest>(Func<string, TRequest, Task<IResult>> handler)
        where TRequest : notnull
    {
        _inner.Update(handler);
        return this;
    }

    public EidosRelationshipRouteBuilder PatchState(Func<string, StateTransitionRequest, IResult> handler) => Transition(handler);

    public EidosRelationshipRouteBuilder PatchState(Func<string, StateTransitionRequest, Task<IResult>> handler) => Transition(handler);

    public EidosRelationshipRouteBuilder PatchProperties<TRequest>(Func<string, TRequest, IResult> handler) where TRequest : notnull => Update(handler);

    public EidosRelationshipRouteBuilder PatchProperties<TRequest>(Func<string, TRequest, Task<IResult>> handler) where TRequest : notnull => Update(handler);

    public EidosRelationshipRouteBuilder Delete(Func<string, IResult> handler)
    {
        _inner.Delete(handler);
        return this;
    }

    public EidosRelationshipRouteBuilder Delete(Func<string, Task<IResult>> handler)
    {
        _inner.Delete(handler);
        return this;
    }

    public EidosRelationshipRouteBuilder WithCollectionPath(string path)
    {
        _inner.WithCollectionPath(path);
        return this;
    }

    public EidosRelationshipRouteBuilder WithItemPath(string path)
    {
        _inner.WithItemPath(path);
        return this;
    }

    private async Task<IResult> BuildExpandedResultAsync(
        IDictionary<string, object?> baseEntity,
        HttpRequest request,
        IReadOnlyDictionary<string, Func<string, Task<object?>>>? participantResolvers = null)
    {
        var expandItems = ParseExpand(request);
        if (expandItems.Count == 0)
        {
            return Results.Ok(baseEntity);
        }

        var unknown = expandItems
            .Where(name => _declaration.Participants.All(p => !string.Equals(p.Name, name, StringComparison.Ordinal)))
            .ToArray();

        if (unknown.Length > 0)
        {
            return Results.BadRequest(new
            {
                message = $"Unknown expand participant(s): {string.Join(", ", unknown)}"
            });
        }

        var expanded = new Dictionary<string, object?>(baseEntity, StringComparer.Ordinal);

        foreach (var participantName in expandItems)
        {
            var participant = _declaration.Participants.Single(p => string.Equals(p.Name, participantName, StringComparison.Ordinal));

            if (!expanded.TryGetValue(participantName, out var refValue) || refValue is not string refKey)
            {
                _reportDiagnostic(new EidosRouteDiagnostic(
                    EidosRouteDiagnosticSeverity.Warning,
                    $"Relationship '{_declaration.Name}' expand '{participantName}' expected a string key field in representation.",
                    EidosResourceType.Relationship,
                    _declaration.Name,
                    EidosOperationType.Get,
                    _declaration.Span));
                continue;
            }

            if (participantResolvers is not null && participantResolvers.TryGetValue(participantName, out var resolver))
            {
                var resolved = await resolver(refKey).ConfigureAwait(false);
                if (resolved is not null)
                {
                    expanded[participantName] = resolved;
                    continue;
                }

                _reportDiagnostic(new EidosRouteDiagnostic(
                    EidosRouteDiagnosticSeverity.Warning,
                    $"Relationship '{_declaration.Name}' expand '{participantName}' could not resolve key '{refKey}' via configured participant resolver.",
                    EidosResourceType.Relationship,
                    _declaration.Name,
                    EidosOperationType.Get,
                    _declaration.Span));
                continue;
            }

            if (_resolveEntity(participant.TypeName, refKey, out var entity))
            {
                expanded[participantName] = entity;
            }
            else
            {
                _reportDiagnostic(new EidosRouteDiagnostic(
                    EidosRouteDiagnosticSeverity.Warning,
                    $"Relationship '{_declaration.Name}' expand '{participantName}' could not resolve entity '{participant.TypeName}' by key '{refKey}'.",
                    EidosResourceType.Relationship,
                    _declaration.Name,
                    EidosOperationType.Get,
                    _declaration.Span));
            }
        }

        return Results.Ok(expanded);
    }

    private IReadOnlyDictionary<string, Func<string, Task<object?>>>? BuildTwoParticipantResolvers(
        Func<string, Task<object?>> firstParticipantEntityResolver,
        Func<string, Task<object?>> secondParticipantEntityResolver)
    {
        if (_declaration.Participants.Count != 2)
        {
            _reportDiagnostic(new EidosRouteDiagnostic(
                EidosRouteDiagnosticSeverity.Error,
                $"Relationship '{_declaration.Name}' GetEntity overload with two participant resolvers requires exactly 2 participants.",
                EidosResourceType.Relationship,
                _declaration.Name,
                EidosOperationType.Get,
                _declaration.Span));
            return null;
        }

        return new Dictionary<string, Func<string, Task<object?>>>(StringComparer.Ordinal)
        {
            [_declaration.Participants[0].Name] = firstParticipantEntityResolver,
            [_declaration.Participants[1].Name] = secondParticipantEntityResolver
        };
    }

    private IEnumerable<AnchoredCollectionPath> BuildAnchoredCollectionPaths()
    {
        var seenPaths = new HashSet<string>(StringComparer.Ordinal);

        foreach (var participant in _declaration.Participants)
        {
            var participantCollection = _options.CollectionSegmentStrategy(participant.TypeName);
            var participantKeyParameter = _options.ItemRouteParameterStrategy(participant.TypeName);
            var relationshipCollection = _options.CollectionSegmentStrategy(_declaration.Name);
            var path = $"/{participantCollection}/{{{participantKeyParameter}}}/{relationshipCollection}";

            if (!seenPaths.Add(path))
            {
                continue;
            }

            yield return new AnchoredCollectionPath(participant.TypeName, participantKeyParameter, path);
        }
    }

    private void RegisterAnchoredListRoute(string path)
    {
        _register(EidosResourceType.Relationship, _declaration.Name, EidosOperationType.List);
        _registerMappedRoute(EidosResourceType.Relationship, _declaration.Name, EidosOperationType.List, path, ["GET"]);
    }

    private static bool TryGetRouteValue(HttpRequest request, string routeParameterName, out string value)
    {
        if (request.RouteValues.TryGetValue(routeParameterName, out var raw) && raw is not null)
        {
            value = Convert.ToString(raw, CultureInfo.InvariantCulture) ?? string.Empty;
            return true;
        }

        value = string.Empty;
        return false;
    }

    private readonly record struct AnchoredCollectionPath(
        string ParticipantTypeName,
        string RouteParameterName,
        string Path);

    private static HashSet<string> ParseExpand(HttpRequest request)
    {
        if (!request.Query.TryGetValue("expand", out var expandValues) || expandValues.Count == 0)
        {
            return [];
        }

        var parsed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var raw in expandValues)
        {
            if (string.IsNullOrEmpty(raw))
            {
                continue;
            }

            foreach (var item in raw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                parsed.Add(item);
            }
        }

        return parsed;
    }

    private static bool TryExtractEntityMap(IResult result, out IDictionary<string, object?> baseEntity)
    {
        if (result is IValueHttpResult { Value: IDictionary<string, object?> value })
        {
            baseEntity = value;
            return true;
        }

        baseEntity = null!;
        return false;
    }
}
