# Eidos — Session Handoff

This is a context summary for continuing work on the Eidos language specification
**and** its .NET reference implementation.

**Read the spec first:** [`SPECIFICATION/eidos-specification-v0.1.md`](SPECIFICATION/eidos-specification-v0.1.md)
(grammar: [`SPECIFICATION/eidos-syntax-v0.1.abnf`](SPECIFICATION/eidos-syntax-v0.1.abnf)).
This document captures decisions, reasoning, and history that are not fully reflected in the spec,
and records where the implementation currently diverges from it.

> **Note on file churn:** the spec file has been renamed more than once
> (`Eidos-spec-v0.1.md` → `eidos-specification-v0.1.md`). If a path here is stale,
> list the directory before assuming a file is missing.

---

## Repository layout

The repo is split into three top-level, separately-licensed parts (see [README.md](README.md)):

| Directory                          | Contents                                                                                   | License      |
| ---------------------------------- | ------------------------------------------------------------------------------------------ | ------------ |
| [SPECIFICATION/](SPECIFICATION/)   | The language spec (`eidos-specification-v0.1.md`) + ABNF grammar (`eidos-syntax-v0.1.abnf`) | CC-BY-SA 4.0 |
| [IMPLEMENTATION/](IMPLEMENTATION/) | .NET reference implementation (parser + ASP.NET Core runtime) and tests                    | AGPL-3.0     |
| [COMMERCIAL/](COMMERCIAL/)         | Commercial licensing, compliance program, pricing                                          | Proprietary  |

---

## What Eidos is

An IDL for HTTP API design where the ontological structure drives the API shape. Not a superset of OpenAPI — it is intended to compile _to_ OpenAPI. The central ideas:

- Relationships between entities are first-class resources with their own URL, identity, properties, and lifecycle (not foreign-key fields)
- Every entity and relationship may declare an explicit lifecycle as a state machine
- State changes happen via `PUT /{id}/_state` — no RPC-style transition endpoints
- Lifecycle coupling between related entities is a first-class declaration, not application logic

---

## Terminology decisions (important — several renames happened)

The language started with UFO terminology and was progressively renamed toward natural English. The spec's §1 now states this explicitly: the terminology is intentionally HTTP-API-centric, and for UFO-familiar readers the mapping is roughly **kind→entity, relator→relationship, relatum→participant, phase→state**.

| Dropped term         | Current term                   | Reason                                                                              |
| -------------------- | ------------------------------ | ----------------------------------------------------------------------------------- |
| `kind`               | `entity`                       | "kind" reads awkwardly in sentences; "entity" is natural and unambiguous in context |
| `relator`            | `relationship`                 | same; "relator" is UFO jargon not needed at the surface                             |
| `relatum` / `relata` | `participant` / `participants` | "relatum" is a loan word; "participant" is clear and pronounceable                  |
| `phase`              | `state`                        | "state" is the everyday word; "phase" survives only inside the parser AST (see below) |
| RAPTOR               | Eidos                          | original codename; Eidos chosen as neutral, no backronym needed                     |

UFO terms (`kind`, `relator`, `endurant`) are still referenced in the spec's concept table as "Corresponds to X in UFO" — but they are **not** surface syntax. The file extension is `.eidos`.

The word `endurant` was deliberately **kept** in the spec (not renamed). It's the term for "an instance of an entity or relationship" — the thing that has identity and lifecycle. The user is comfortable with it and it has no good everyday synonym that isn't already overloaded.

---

## Design decisions not fully captured in the spec

**PATCH vs PUT /\_state — split operations**
Property updates and state transitions are two distinct operations on different URLs:

- `PATCH /{id}` — partial property update only. Never changes the state.
- `PUT /{id}/_state` — lifecycle transition only. Body: `{ "state": "<TargetState>" }`.

This was introduced to resolve an `AmbiguousMatchException` in the prototype where both operations were registered as PATCH on the same route (see [`IMPLEMENTATION/TODO/ambiguous-patch.md`](IMPLEMENTATION/TODO/ambiguous-patch.md)). The split also makes intent explicit to API clients. It is implemented: the runtime maps `Transition(...)` → `PUT /{id}/_state` and `Update(...)` → `PATCH /{id}`, with `StateTransitionRequest(string State, string? Transition)` as the body shape.

> An open TODO ([`json-patch-for-property-patch.mc`](IMPLEMENTATION/TODO/json-patch-for-property-patch.mc)) proposes using a JSON Patch (RFC 5789) profile for the property-PATCH routes. Not yet acted on.

`PUT /_state` is idempotent: a second identical request when already in the target state returns `409 Conflict`. This does not violate idempotency — idempotency guarantees server state after N requests equals state after 1, not that the response code is identical.

**Non-deterministic state machines — transition names are the discriminator**
When two transitions share the same (source, target) pair but have distinct names, the `PUT /_state` body requires a `transition` field whose value is a transition name; the compiler derives this automatically. There is no `discriminators { }` block — it was removed from the grammar entirely.

```json
{ "state": "Terminated", "transition": "resign" }
```

`LifecycleAnalyzer` classifies each machine as `Deterministic` / `NonDeterministic` / `Ambiguous` based purely on transition names: a shared (source, target) pair reached by transitions with **distinct** names → `NonDeterministic` (warning; the `transition` field disambiguates); reached by transitions sharing the **same** name → `Ambiguous` (error; the name can't disambiguate). Spec, ABNF, AST, parser, and analyzer are aligned on this — no `Discriminator*` syntax nodes remain.

**Reserved representation fields**
`_self`, `_type`, `_state` are the three reserved system fields emitted by the compiler on every representation. Designers do not declare them. The concept is _state_ in the schema language (formerly _phase_) and surfaces as `_state` in the HTTP representation.

**Relationship URL ownership**
The `relationship` type owns the canonical URL (e.g., `/employments/42`). Both participant entities expose projection collection endpoints (`/persons/7/employments`, `/organizations/3/employments`) that return the full relationship representation including `_self` pointing to the canonical URL. Clients never need to construct the canonical URL — it is always present in the response. In the runtime this is `ListByParticipant(...)`, which auto-derives the anchored paths from the `between` participants.

**`?expand` parameter**
Every relationship endpoint generates a `?expand=<participant>[,<participant>]` query parameter. By default participants are represented as URLs/keys; `?expand` embeds them inline. Participant names in the expand list correspond to the names declared in the `between` clause. Unknown names → 400. Implemented in `EidosRelationshipRouteBuilder.BuildExpandedResult`.

**File importability rule**
Any `.eidos` file may mix `entity`, `relationship`, `mixin`, and `archetype` declarations freely. The restriction is at the _import boundary_: a file can only be imported as a library if it contains exclusively `archetype` declarations. This was chosen over a hard file-type separation to avoid being unnecessarily restrictive while still allowing archetype libraries to be distributed independently.

**Archetype non-composability of state-free archetypes**
`ReadOnly`, `Immutable`, and `WriteOnce` have no states and no transitions — they are HTTP surface constraints, not lifecycle shapes. Composing them with lifecycle-bearing archetypes produces a logical contradiction (e.g., `ReadOnly + Activatable` would have states but no way to change state via HTTP). These are explicitly non-composable. The `composable` keyword and `+` operator are intended only for archetypes with at least one state. Full composition semantics (product automaton rules) are TBD §5.3.

**Coupling direction**
Coupling rules are declared _on the relationship_, not on the entity. The syntax `on employer.state -> Dissolved : terminate` inside a `relationship` block means: when the employer participant transitions to Dissolved, invoke the `terminate` transition on this relationship. This keeps coupling logic co-located with the relationship that owns it.

**UFO positioning**
UFO is mentioned in the abstract and concept table as the conceptual grounding, but it is not positioned as the "driving force." The motivation section leads with the concrete problems (no relationship identity, no declared states, no lifecycle coupling) and only then mentions UFO as the source of vocabulary. The spec is explicitly written so that UFO familiarity is not required.

---

## What has been implemented so far

The implementation lives in [IMPLEMENTATION/](IMPLEMENTATION/) (`Eidos.slnx`, .NET `net10.0`). Three projects plus a sample and tests:

**`src/EidosCore`** (assembly `EidosCore`, namespaces `Eidos.Core` / `Eidos.Core.Analysis`) — hand-written scanner + recursive-descent parser, no codegen dependency. (Formerly `EidosParser` / `Eidos.Parser`; renamed since it holds more than the parser.)
- `EidosScanner` (`ref struct`, `SequenceReader<char>`) tokenizes; `EidosGrammarParser` builds the AST.
- `EidosSyntax.cs` — the AST as C# records (`state`-named nodes: `StateSetSyntax`, `InitialState`, etc.).
- `EidosSchemaReader` — async `PipeReader` entry point for parsing from file/stream.
- `Analysis/LifecycleAnalyzer` — the §5.1 determinism analysis (Deterministic / NonDeterministic / Ambiguous, keyed on transition names).

**`src/Eidos.AspNetCore`** — a **runtime** route-mapping library, *not* an OpenAPI emitter.
- `MapEidos(document, configure, ...)` maps a parsed AST onto ASP.NET Core minimal-API endpoints via a fluent builder (`Entity(...)`, `Relationship(...)`).
- Validates route coverage against an `IEidosOperationPolicy` (default: lifecycle-bearing resources require List/Get/Post/PutState/Delete; otherwise List/Get/Post) and emits `EidosRouteDiagnostic`s; `FailOnError` throws on startup.
- `MapMetadataEndpoint("/")` serves a machine- or `?format=plain` list of mapped routes.
- V0.1 key semantics: item routes are `/{collection}/{key}` (`ItemRouteParameterStrategy => "key"`); `CollectionSegmentStrategy` does naive pluralization.

**`samples/Eidos.Sample.AspNetCore`** — runnable demo (`dotnet run --project samples/Eidos.Sample.AspNetCore`). In-memory Person + Employment store, schema parsed from an inline string, exercised by [`IMPLEMENTATION/test.http`](IMPLEMENTATION/test.http). Runs on `http://localhost:5135`.

**`tests/`** — xUnit tests for the parser (`ParserTests`, `LifecycleAnalyzerTests`, `EidosSchemaReaderTests`) and the route mapper (`EidosMapBuilderTests`).

> **Big-picture gap:** the spec describes a compiler producing **OpenAPI 3.x**, state-machine diagrams, and event catalogs. The current implementation instead parses Eidos and maps it to **live ASP.NET Core routes** with coverage validation. There is no OpenAPI/AsyncAPI generation yet — that is the major unbuilt piece.

---

## Open questions (from §7 of spec, with added context)

**§5.3 Archetype composition** — biggest open item. Product automaton construction, compatible state namespaces, which built-ins are composable. May be deferred from v1. (The analyzer currently emits a warning and skips analysis for any `A + B` reference.)

**§7.2 Namespace and import** — whether namespaces map to URL path prefixes is the key unresolved question. Could significantly affect the generated OpenAPI server URL structure. Cross-file archetype imports are also not yet resolved by the analyzer (unresolved refs → warning).

**§7.3 Guard predicate language** — currently guards are named predicates resolved externally (the compiler emits `x-eidos-guard` OpenAPI extensions). Whether to add a structural expression language (property comparisons, role membership) is open. The grammar/AST already model guard expressions (`&&`, `||`, `!`, `name.state in [...]`).

**§7.4 Event emission** — `emits: TypeName` is reserved in transition options. Full AsyncAPI/CloudEvents generation is planned but not designed.

**§7.5 History resource** — opt-in `GET /{entity}/{id}/history` returning transition log. Natural fit given explicit state machines. Implementation pattern not yet designed.

**§7.9 Mixin lifecycle fragments** — whether a `mixin` can contribute partial state machine fragments (not just properties) to including types. The AST already has `MixinLifecycleFragmentMemberSyntax` and the analyzer processes it, but the spec still lists mixins as properties-only / lifecycle-archetype-only.

---

## Tone / working style notes

- The user is precise about terminology and will push back on loose language
- Decisions are made incrementally — don't anticipate too far ahead
- The spec is the source of truth; this file supplements it, not replaces it
- When something is TBD, leave it as TBD rather than filling it in speculatively
- Files get renamed/moved between sessions — verify paths before assuming
