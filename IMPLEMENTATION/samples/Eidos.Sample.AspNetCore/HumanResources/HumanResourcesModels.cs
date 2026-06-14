namespace Eidos.Sample.HumanResources;

public sealed record PersonDto(string Key, string Name, string Email, string State);


// JSON Patch (RFC 6902) targets. These are deliberately restricted mutable POCOs exposing only the
// properties a client may patch — the MS-recommended mitigation for JSON Patch's inherent risks. Key is
// identity and State is lifecycle-only (changed via PUT /_state), so neither is patchable: an op against
// /key or /state hits a path that doesn't exist here and is rejected (400).
public sealed record PersonPatch(string? Name, string? Email);

public sealed record PersonCreateRequest(string? Key, string Name, string Email);


public sealed record EmploymentCreateRequest(string? Key, string EmployeeKey, string EmployerKey, string Title);

public sealed record EmploymentDto(string Key, string EmployeeKey, string EmployerKey, string Title, string State);

public sealed record EmploymentPatch(string? Title);
