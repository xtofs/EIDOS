# Extract `EidosEndurantRouteBuilder` — shared entity/relationship route surface — OPEN

**Status: open / future cleanup.** Not a bug; a design smell with a known fix.

## The trade-off

`EidosRelationshipRouteBuilder` reuses `EidosEntityRouteBuilder` as a private `_inner`
(`IMPLEMENTATION/src/Eidos.AspNetCore/EidosMapBuilder.cs`). To make an entity builder stand in for a
relationship it is constructed with a **synthetic `EntityDeclarationSyntax`** carrying the relationship's name
and a **re-tagged `registerMappedRoute`** (so routes record as `EidosResourceType.Relationship`). Consequences:

- The class called `EidosEntityRouteBuilder` actually implements the route surface for both kinds of resource —
  the spec's umbrella term (§2 concept table) is **endurant**, so the name is too narrow for the role it plays.
- Every shared verb on the relationship builder (`List` / `Create` / `Get` / `Transition` / `Update` /
  `Delete`) is boilerplate that forwards to `_inner` and returns `this`.
- There are **two `Context()` methods** — one on each builder — that must independently produce the same
  `ResourceContext` (name + collection segment + lifecycle) and must be kept in sync by hand.
- The synthetic AST node is a hack: an entity declaration faked to host a relationship.

## The fix

Extract the shared core into `EidosEndurantRouteBuilder` that owns the common verbs plus the path helpers
(`CollectionPath` / `ItemPath` / `StatePath`) and the single `Context()`. `EidosEntityRouteBuilder` and
`EidosRelationshipRouteBuilder` become thin specializations:

- Entity adds the participant-expand entity-resolver registration (`GetSingle` / `GetEntity`).
- Relationship adds `ListByParticipant`, the participant-aware `GetSingle`, and the `?expand` flow
  (`ExpandAndWriteAsync`).

This removes `_inner`, the synthetic `EntityDeclarationSyntax`, and the duplicated `Context()`.

**Wrinkle to settle when implementing:** the fluent verbs must return the concrete builder type to keep the
chain typed (so `.Relationship(e => e.List(…).ListByParticipant(…))` still sees relationship-only methods). The
clean way is a self-referential generic base
(`abstract class EidosEndurantRouteBuilder<TSelf> where TSelf : EidosEndurantRouteBuilder<TSelf>` with
`protected abstract TSelf Self { get; }` and `return Self`). Alternative: keep composition but rename the
shared core to `EidosEndurantRouteBuilder` and have both builders wrap it — less type machinery, but the
forwarding boilerplate stays. Decide at implementation time.

Driver: discovered while refactoring the builder's route bookkeeping/deferred mapping (see git history).
