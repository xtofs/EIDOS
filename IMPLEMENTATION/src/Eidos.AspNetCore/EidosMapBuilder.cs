using Eidos.Core;
using Eidos.Core.OpenApi;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.OpenApi;
using System.Globalization;
using System.IO;

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

    private readonly EidosDocumentSyntax _document;

    internal EidosMapBuilder(
        IEndpointRouteBuilder endpoints,
        EidosDocumentSyntax document,
        EidosRouteMappingOptions options,
        IEidosOperationPolicy operationPolicy)
    {
        _endpoints = endpoints;
        _options = options;
        _operationPolicy = operationPolicy;
        _document = document;

        _entities = document.Declarations
            .OfType<EntityDeclarationSyntax>()
            .ToDictionary(d => d.Name, StringComparer.Ordinal);

        _relationships = document.Declarations
            .OfType<RelationshipDeclarationSyntax>()
            .ToDictionary(d => d.Name, StringComparer.Ordinal);

        // A declaration's `url:` hint overrides the default collection segment for its routes
        // (own collection, item, and participant projections all resolve through this strategy).
        var configuredSegment = _options.CollectionSegmentStrategy;
        _options.CollectionSegmentStrategy = typeName => UrlHintSegment(typeName) ?? configuredSegment(typeName);
    }

    private string? UrlHintSegment(string typeName)
    {
        if (_entities.TryGetValue(typeName, out var entity)
            && entity.Members.OfType<EntityUrlHintMemberSyntax>().FirstOrDefault()?.UrlHint.Value is { Length: > 0 } entityHint)
        {
            return entityHint;
        }

        if (_relationships.TryGetValue(typeName, out var relationship)
            && relationship.Members.OfType<RelationshipUrlHintMemberSyntax>().FirstOrDefault()?.UrlHint.Value is { Length: > 0 } relationshipHint)
        {
            return relationshipHint;
        }

        return null;
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
            declaration.Members.OfType<EntityLifecycleMemberSyntax>().Any(),
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

    /// <summary>Builds the generated OpenAPI document (§5.2) for this schema.</summary>
    public OpenApiDocument BuildOpenApiDocument(ApiInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        return OpenApiDocumentGenerator.Generate(_document, info);
    }

    /// <summary>Serves the generated OpenAPI 3.0 document as JSON at <paramref name="pattern"/>.</summary>
    public EidosMapBuilder MapOpenApiEndpoint(string pattern = "/openapi.json", ApiInfo? info = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);
        var apiInfo = info ?? new ApiInfo("Eidos API", "0.1");

        _endpoints.MapGet(pattern, () =>
        {
            var document = OpenApiDocumentGenerator.Generate(_document, apiInfo);
            using var stringWriter = new StringWriter();
            document.SerializeAsV3(new OpenApiJsonWriter(stringWriter));
            return Results.Text(stringWriter.ToString(), "application/json");
        });

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
            .Select(m => $"### {r.Operation} {r.ResourceType} {r.ResourceName}\n{m} {{{{baseUrl}}}}{r.Path} body: {r.Operation} = {BuildOperationHint(r.Operation)}")));
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
    private readonly bool _hasLifecycle;
    private readonly Action<EidosResourceType, string, EidosOperationType> _register;
    private readonly Action<EidosResourceType, string, EidosOperationType, string, IReadOnlyList<string>> _registerMappedRoute;
    private readonly Action<string, Func<string, object?>> _registerEntityResolver;

    internal EidosEntityRouteBuilder(
        IEndpointRouteBuilder endpoints,
        EntityDeclarationSyntax declaration,
        EidosRouteMappingOptions options,
        bool hasLifecycle,
        Action<EidosResourceType, string, EidosOperationType> register,
        Action<EidosResourceType, string, EidosOperationType, string, IReadOnlyList<string>> registerMappedRoute,
        Action<string, Func<string, object?>> registerEntityResolver)
    {
        _endpoints = endpoints;
        _declaration = declaration;
        _options = options;
        _hasLifecycle = hasLifecycle;
        _register = register;
        _registerMappedRoute = registerMappedRoute;
        _registerEntityResolver = registerEntityResolver;
    }

    /// <summary>The reserved-field context for this resource (TypeName, collection segment, lifecycle).</summary>
    internal ResourceContext Context() => new(_declaration.Name, CollectionPath().TrimStart('/'), _hasLifecycle);

    /// <summary>Maps a raw GET on the item path (used by the relationship builder for the ?expand flow).</summary>
    internal void MapItemGet(Func<string, HttpRequest, Task<IResult>> handler)
    {
        var path = ItemPath();
        _endpoints.MapGet(path, handler);
        Register(EidosOperationType.Get);
        RegisterMappedRoute(EidosOperationType.Get, path, "GET");
    }

    public EidosEntityRouteBuilder List<T>(Func<Task<Response<T>>> handler)
    {
        var path = CollectionPath();
        var context = Context();
        _endpoints.MapGet(path, async () => RepresentationWriter.Write(await handler().ConfigureAwait(false), context));
        Register(EidosOperationType.List);
        RegisterMappedRoute(EidosOperationType.List, path, "GET");
        return this;
    }

    public EidosEntityRouteBuilder List<T>(Func<Response<T>> handler) => List(() => Task.FromResult(handler()));

    public EidosEntityRouteBuilder Create<TRequest, T>(Func<TRequest, Task<Response<T>>> handler)
    {
        var path = CollectionPath();
        var context = Context();
        _endpoints.MapPost(path, async (TRequest body) => RepresentationWriter.Write(await handler(body).ConfigureAwait(false), context));
        Register(EidosOperationType.Create);
        RegisterMappedRoute(EidosOperationType.Create, path, "POST");
        return this;
    }

    public EidosEntityRouteBuilder Create<TRequest, T>(Func<TRequest, Response<T>> handler) =>
        Create<TRequest, T>((TRequest body) => Task.FromResult(handler(body)));

    public EidosEntityRouteBuilder Create<T>(Func<Task<Response<T>>> handler)
    {
        var path = CollectionPath();
        var context = Context();
        _endpoints.MapPost(path, async () => RepresentationWriter.Write(await handler().ConfigureAwait(false), context));
        Register(EidosOperationType.Create);
        RegisterMappedRoute(EidosOperationType.Create, path, "POST");
        return this;
    }

    public EidosEntityRouteBuilder Create<T>(Func<Response<T>> handler) => Create(() => Task.FromResult(handler()));

    public EidosEntityRouteBuilder Get<T>(Func<string, Task<Response<T>>> handler)
    {
        var path = ItemPath();
        var context = Context();
        _endpoints.MapGet(path, async (string key) => RepresentationWriter.Write(await handler(key).ConfigureAwait(false), context));
        Register(EidosOperationType.Get);
        RegisterMappedRoute(EidosOperationType.Get, path, "GET");
        return this;
    }

    public EidosEntityRouteBuilder Get<T>(Func<string, Response<T>> handler) => Get((string key) => Task.FromResult(handler(key)));

    // Like Get, but also registers a resolver so relationship ?expand can embed this entity by key.
    public EidosEntityRouteBuilder GetEntity<T>(Func<string, Task<Response<T>>> handler)
    {
        _registerEntityResolver(_declaration.Name, key => handler(key).GetAwaiter().GetResult().Value);
        return Get(handler);
    }

    public EidosEntityRouteBuilder GetEntity<T>(Func<string, Response<T>> handler)
    {
        _registerEntityResolver(_declaration.Name, key => handler(key).Value);
        return Get(handler);
    }

    public EidosEntityRouteBuilder Transition<T>(Func<string, StateTransitionRequest, Task<Response<T>>> handler)
    {
        var path = StatePath();
        var context = Context();
        _endpoints.MapMethods(path, ["PUT"], async (string key, StateTransitionRequest request) =>
            RepresentationWriter.Write(await handler(key, request).ConfigureAwait(false), context));
        Register(EidosOperationType.PutState);
        RegisterMappedRoute(EidosOperationType.PutState, path, "PUT");
        return this;
    }

    public EidosEntityRouteBuilder Transition<T>(Func<string, StateTransitionRequest, Response<T>> handler) =>
        Transition<T>((string key, StateTransitionRequest request) => Task.FromResult(handler(key, request)));

    public EidosEntityRouteBuilder Update<TRequest, T>(Func<string, TRequest, Task<Response<T>>> handler)
        where TRequest : notnull
    {
        var path = ItemPath();
        var context = Context();
        _endpoints.MapMethods(path, ["PATCH"], async (string key, TRequest body) =>
                RepresentationWriter.Write(await handler(key, body).ConfigureAwait(false), context))
            .Accepts<TRequest>(JsonPatchMediaType);
        Register(EidosOperationType.PatchProperties);
        RegisterMappedRoute(EidosOperationType.PatchProperties, path, "PATCH");
        return this;
    }

    public EidosEntityRouteBuilder Update<TRequest, T>(Func<string, TRequest, Response<T>> handler) where TRequest : notnull =>
        Update<TRequest, T>((string key, TRequest body) => Task.FromResult(handler(key, body)));

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

        var hasLifecycle = declaration.Members.OfType<RelationshipLifecycleMemberSyntax>().Any();

        _inner = new EidosEntityRouteBuilder(
            endpoints,
            new EntityDeclarationSyntax(declaration.Name, [], [], declaration.Annotations, declaration.Span),
            options,
            hasLifecycle,
            (_, _, operation) => register(EidosResourceType.Relationship, declaration.Name, operation),
            (_, _, operation, path, methods) => registerMappedRoute(EidosResourceType.Relationship, declaration.Name, operation, path, methods),
            (_, _) => { });
    }

    private ResourceContext Context() =>
        new(_declaration.Name, _options.CollectionSegmentStrategy(_declaration.Name), _declaration.Members.OfType<RelationshipLifecycleMemberSyntax>().Any());

    public EidosRelationshipRouteBuilder List<T>(Func<Task<Response<T>>> handler)
    {
        _inner.List(handler);
        return this;
    }

    public EidosRelationshipRouteBuilder List<T>(Func<Response<T>> handler)
    {
        _inner.List(handler);
        return this;
    }

    public EidosRelationshipRouteBuilder ListByParticipant<T>(Func<string, string, Task<Response<T>>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        var context = Context();

        foreach (var anchored in BuildAnchoredCollectionPaths())
        {
            var participantTypeName = anchored.ParticipantTypeName;
            var routeParameterName = anchored.RouteParameterName;

            _endpoints.MapGet(anchored.Path, async (HttpRequest request) =>
            {
                if (!TryGetRouteValue(request, routeParameterName, out var key))
                {
                    return Results.BadRequest(new { message = $"Missing route value '{routeParameterName}'." });
                }

                return RepresentationWriter.Write(await handler(participantTypeName, key).ConfigureAwait(false), context);
            });
            RegisterAnchoredListRoute(anchored.Path);
        }

        return this;
    }

    public EidosRelationshipRouteBuilder ListByParticipant<T>(Func<string, string, Response<T>> handler) =>
        ListByParticipant<T>((string participantTypeName, string key) => Task.FromResult(handler(participantTypeName, key)));

    public EidosRelationshipRouteBuilder Create<TRequest, T>(Func<TRequest, Task<Response<T>>> handler)
    {
        _inner.Create(handler);
        return this;
    }

    public EidosRelationshipRouteBuilder Create<TRequest, T>(Func<TRequest, Response<T>> handler)
    {
        _inner.Create(handler);
        return this;
    }

    public EidosRelationshipRouteBuilder Create<T>(Func<Task<Response<T>>> handler)
    {
        _inner.Create(handler);
        return this;
    }

    public EidosRelationshipRouteBuilder Create<T>(Func<Response<T>> handler)
    {
        _inner.Create(handler);
        return this;
    }

    public EidosRelationshipRouteBuilder Get<T>(Func<string, Task<Response<T>>> handler)
    {
        _inner.Get(handler);
        return this;
    }

    public EidosRelationshipRouteBuilder Get<T>(Func<string, Response<T>> handler)
    {
        _inner.Get(handler);
        return this;
    }

    /// <summary>
    /// Maps the canonical relationship item GET. The handler returns the representation as a dictionary whose
    /// participant-named fields hold participant keys; <c>?expand</c> embeds the resolved participants inline.
    /// Reserved fields are added to the top-level representation by the framework.
    /// </summary>
    public EidosRelationshipRouteBuilder GetEntity(Func<string, Task<Response<IDictionary<string, object?>>>> handler) =>
        GetEntity(handler, participantResolvers: null);

    public EidosRelationshipRouteBuilder GetEntity(
        Func<string, Task<Response<IDictionary<string, object?>>>> handler,
        Func<string, Task<object?>> firstParticipantEntityResolver,
        Func<string, Task<object?>> secondParticipantEntityResolver)
    {
        ArgumentNullException.ThrowIfNull(firstParticipantEntityResolver);
        ArgumentNullException.ThrowIfNull(secondParticipantEntityResolver);
        return GetEntity(handler, BuildTwoParticipantResolvers(firstParticipantEntityResolver, secondParticipantEntityResolver));
    }

    private EidosRelationshipRouteBuilder GetEntity(
        Func<string, Task<Response<IDictionary<string, object?>>>> handler,
        IReadOnlyDictionary<string, Func<string, Task<object?>>>? participantResolvers)
    {
        ArgumentNullException.ThrowIfNull(handler);
        var context = Context();

        _inner.MapItemGet(async (string key, HttpRequest request) =>
        {
            var result = await handler(key).ConfigureAwait(false);
            if (((IEidosRepresentation)result).PassThrough is not null || result.Value is null)
            {
                return RepresentationWriter.Write(result, context);
            }

            return await ExpandAndWriteAsync(result.Value, request, context, participantResolvers).ConfigureAwait(false);
        });
        return this;
    }

    public EidosRelationshipRouteBuilder Transition<T>(Func<string, StateTransitionRequest, Task<Response<T>>> handler)
    {
        _inner.Transition(handler);
        return this;
    }

    public EidosRelationshipRouteBuilder Transition<T>(Func<string, StateTransitionRequest, Response<T>> handler)
    {
        _inner.Transition(handler);
        return this;
    }

    public EidosRelationshipRouteBuilder Update<TRequest, T>(Func<string, TRequest, Task<Response<T>>> handler)
        where TRequest : notnull
    {
        _inner.Update(handler);
        return this;
    }

    public EidosRelationshipRouteBuilder Update<TRequest, T>(Func<string, TRequest, Response<T>> handler)
        where TRequest : notnull
    {
        _inner.Update(handler);
        return this;
    }

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

    private async Task<IResult> ExpandAndWriteAsync(
        IDictionary<string, object?> baseEntity,
        HttpRequest request,
        ResourceContext context,
        IReadOnlyDictionary<string, Func<string, Task<object?>>>? participantResolvers)
    {
        var expandItems = ParseExpand(request);
        if (expandItems.Count > 0)
        {
            var unknown = expandItems
                .Where(name => _declaration.Participants.All(p => !string.Equals(p.Name, name, StringComparison.Ordinal)))
                .ToArray();

            if (unknown.Length > 0)
            {
                return Results.BadRequest(new { message = $"Unknown expand participant(s): {string.Join(", ", unknown)}" });
            }

            var expanded = new Dictionary<string, object?>(baseEntity, StringComparer.Ordinal);

            foreach (var participantName in expandItems)
            {
                var participant = _declaration.Participants.Single(p => string.Equals(p.Name, participantName, StringComparison.Ordinal));

                if (!expanded.TryGetValue(participantName, out var refValue) || refValue is not string refKey)
                {
                    WarnExpand(participantName, "expected a string key field in the representation.");
                    continue;
                }

                if (participantResolvers is not null && participantResolvers.TryGetValue(participantName, out var resolver))
                {
                    var resolved = await resolver(refKey).ConfigureAwait(false);
                    if (resolved is not null)
                    {
                        expanded[participantName] = resolved;
                    }
                    else
                    {
                        WarnExpand(participantName, $"could not resolve key '{refKey}' via the configured participant resolver.");
                    }

                    continue;
                }

                if (_resolveEntity(participant.TypeName, refKey, out var entity))
                {
                    expanded[participantName] = entity;
                }
                else
                {
                    WarnExpand(participantName, $"could not resolve entity '{participant.TypeName}' by key '{refKey}'.");
                }
            }

            baseEntity = expanded;
        }

        return RepresentationWriter.Write(Response.Ok(baseEntity), context);
    }

    private void WarnExpand(string participantName, string detail) =>
        _reportDiagnostic(new EidosRouteDiagnostic(
            EidosRouteDiagnosticSeverity.Warning,
            $"Relationship '{_declaration.Name}' expand '{participantName}' {detail}",
            EidosResourceType.Relationship,
            _declaration.Name,
            EidosOperationType.Get,
            _declaration.Span));

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

}
