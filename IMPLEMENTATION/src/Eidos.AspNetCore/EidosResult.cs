using Microsoft.AspNetCore.Http;

namespace Eidos.AspNetCore;

/// <summary>
/// Non-generic view of an <see cref="EidosResult{T}"/>, used by the route builder to enrich and serialize
/// the representation with the reserved fields (<c>_self</c>/<c>_type</c>/<c>_state</c>).
/// </summary>
internal interface IEidosRepresentation
{
    /// <summary>The success value to serialize and enrich, or null for a pass-through result.</summary>
    object? Value { get; }

    int StatusCode { get; }

    string? Location { get; }

    IReadOnlyList<KeyValuePair<string, string>> Headers { get; }

    /// <summary>A non-representation result (error/redirect/etc.) to execute verbatim, or null.</summary>
    IResult? PassThrough { get; }
}

/// <summary>
/// The result type returned by Eidos representation handlers. On success it carries the entity value (which the
/// framework serializes and decorates with the reserved <c>_self</c>/<c>_type</c>/<c>_state</c> fields); for
/// errors it wraps a plain <see cref="IResult"/> that is executed verbatim. Either way HTTP status, headers, and
/// a <c>Location</c> remain controllable. Use the <see cref="EidosResult"/> factory methods to construct one.
/// </summary>
public sealed class EidosResult<T> : IResult, IEidosRepresentation
{
    private readonly List<KeyValuePair<string, string>> _headers = [];

    internal EidosResult(T? value, int statusCode, string? location, IResult? passThrough)
    {
        Value = value;
        StatusCode = statusCode;
        Location = location;
        PassThrough = passThrough;
    }

    public T? Value { get; }

    public int StatusCode { get; }

    public string? Location { get; }

    public IResult? PassThrough { get; }

    object? IEidosRepresentation.Value => Value;

    IReadOnlyList<KeyValuePair<string, string>> IEidosRepresentation.Headers => _headers;

    /// <summary>Adds a response header, returning the same instance for chaining.</summary>
    public EidosResult<T> WithHeader(string name, string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        _headers.Add(new KeyValuePair<string, string>(name, value));
        return this;
    }

    /// <summary>
    /// Safety net for when an <see cref="EidosResult{T}"/> is executed directly rather than through the Eidos
    /// route builder: writes the value (or pass-through) without reserved-field enrichment.
    /// </summary>
    public async Task ExecuteAsync(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ApplyHeaders(httpContext);

        if (PassThrough is not null)
        {
            await PassThrough.ExecuteAsync(httpContext).ConfigureAwait(false);
            return;
        }

        if (Location is not null)
        {
            httpContext.Response.Headers.Location = Location;
        }

        var result = Value is null
            ? Results.StatusCode(StatusCode)
            : Results.Json(Value, statusCode: StatusCode);
        await result.ExecuteAsync(httpContext).ConfigureAwait(false);
    }

    private void ApplyHeaders(HttpContext httpContext)
    {
        foreach (var header in _headers)
        {
            httpContext.Response.Headers.Append(header.Key, header.Value);
        }
    }
}

/// <summary>Factory methods for <see cref="EidosResult{T}"/>.</summary>
public static class EidosResult
{
    /// <summary>200 OK carrying a representation (single value or a collection).</summary>
    public static EidosResult<T> Ok<T>(T value) => new(value, StatusCodes.Status200OK, location: null, passThrough: null);

    /// <summary>201 Created carrying the new representation, optionally with a Location header.</summary>
    public static EidosResult<T> Created<T>(T value, string? location = null) =>
        new(value, StatusCodes.Status201Created, location, passThrough: null);

    /// <summary>404 Not Found (no representation).</summary>
    public static EidosResult<T> NotFound<T>() => new(default, StatusCodes.Status404NotFound, null, Results.NotFound());

    /// <summary>409 Conflict, optionally with a problem body.</summary>
    public static EidosResult<T> Conflict<T>(object? problem = null) =>
        new(default, StatusCodes.Status409Conflict, null, problem is null ? Results.Conflict() : Results.Conflict(problem));

    /// <summary>400 Bad Request, optionally with an error body.</summary>
    public static EidosResult<T> BadRequest<T>(object? error = null) =>
        new(default, StatusCodes.Status400BadRequest, null, error is null ? Results.BadRequest() : Results.BadRequest(error));

    /// <summary>An RFC 7807 problem response with the given status code.</summary>
    public static EidosResult<T> Problem<T>(string? detail = null, int statusCode = StatusCodes.Status400BadRequest) =>
        new(default, statusCode, null, Results.Problem(detail: detail, statusCode: statusCode));

    /// <summary>Passes an arbitrary <see cref="IResult"/> through verbatim (no reserved-field enrichment).</summary>
    public static EidosResult<T> FromResult<T>(IResult result) => new(default, StatusCodes.Status200OK, null, result);
}
