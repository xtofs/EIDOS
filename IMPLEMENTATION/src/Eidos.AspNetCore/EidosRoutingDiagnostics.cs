using Eidos.Core;

namespace Eidos.AspNetCore;

public enum EidosRouteDiagnosticSeverity
{
    Warning,
    Error
}

public enum EidosResourceType
{
    Entity,
    Relationship
}

public enum EidosOperationType
{
    List,
    Get,
    Post,
    PutState,
    PatchProperties,
    Delete
}

public sealed record EidosRouteDiagnostic(
    EidosRouteDiagnosticSeverity Severity,
    string Message,
    EidosResourceType ResourceType,
    string ResourceName,
    EidosOperationType? Operation,
    SourceSpan? Span);
