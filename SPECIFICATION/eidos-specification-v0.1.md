# Eidos

**Language Specification — Working Draft**
Version 0.1 · June 2026
License: [CC-BY-SA 4.0](LICENSE-SPEC.txt)

---

## Abstract

Eidos is a language for designing HTTP APIs in which relationships between entities are first-class resources with their own identity and lifecycle, and the lifecycle of every entity is an explicit state machine rather than an implicit convention baked into documentation or client code.

From an Eidos schema, a compiler produces OpenAPI 3.x specifications, state-machine diagrams, and optionally event-catalog definitions. The language's conceptual model draws on the Unified Foundational Ontology (UFO), particularly its treatment of relationships as reified entities, states, and roles.

This document specifies the language: its goals, its conceptual model, its grammar (in ABNF), and the mapping rules from Eidos constructs to HTTP/OpenAPI surface.

> **Status:** Working draft. Sections marked ⬜ TBD are open design questions. Grammar rules marked [DRAFT] are subject to change.

---

## Table of Contents

1. [Motivation](#1-motivation)
2. [Core Concepts](#2-core-concepts)
   - 2.1 [Relationships as Endurants](#21-relationships-as-endurants)
   - 2.2 [Lifecycle as a State Machine](#22-lifecycle-as-a-state-machine)
   - 2.3 [Archetype Library](#23-archetype-library)
3. [Language Structure](#3-language-structure)
   - 3.1 [File Types](#31-file-types)
   - 3.2 [Namespace and Import](#32-namespace-and-import)
   - 3.3 [Top-level Declarations](#33-top-level-declarations)
4. [Grammar (ABNF)](#4-grammar-abnf)
   - 4.1 [Lexical Rules](#41-lexical-rules)
   - 4.2 [Top-level Document](#42-top-level-document)
   - 4.3 [Entity Declaration](#43-entity-declaration)
   - 4.4 [Relationship Declaration](#44-relationship-declaration)
   - 4.5 [Properties Block](#45-properties-block)
   - 4.6 [Lifecycle Clause](#46-lifecycle-clause)
   - 4.7 [Coupling Block](#47-coupling-block)
   - 4.8 [Mixin Declaration](#48-mixin-declaration)
   - 4.9 [Archetype Declaration](#49-archetype-declaration)
   - 4.10 [Role Declaration](#410-role-declaration)
   - 4.11 [Annotations and Doc Comments](#411-annotations-and-doc-comments)
5. [Compiler Semantics](#5-compiler-semantics)
   - 5.1 [Determinism Check](#51-determinism-check)
   - 5.2 [HTTP Surface Generation](#52-http-surface-generation)
   - 5.3 [Archetype Composition](#53-archetype-composition)
   - 5.4 [Guard Resolution](#54-guard-resolution)
6. [Complete Example](#6-complete-example)
7. [Open Design Questions](#7-open-design-questions)
8. [Relationship to Existing Specifications](#8-relationship-to-existing-specifications)

- [Appendix A — Built-in Archetype Definitions](#appendix-a--built-in-archetype-definitions)

---

## 1 Motivation

Conventional API description languages (OpenAPI, GraphQL SDL, gRPC Protobuf) describe data shapes and operation signatures well. What they do not describe is structure at the level of meaning:

- A relationship between two entities is typically represented as a foreign-key field. It has no identity of its own, no URL, no properties, and no lifecycle. It cannot be queried, updated, or reasoned about directly.
- The states an entity can be in are not declared. Valid operation sequences — which actions are legal in which state — are buried in prose documentation or left for clients to discover by trial and error.
- When the lifecycle of one entity depends on the lifecycle of another (a contract cannot be active if its counterparty has been dissolved), there is no language to express that dependency. It becomes implicit application logic.

Eidos addresses these gaps directly. Relationships are modeled as first-class entities — with their own identity, properties, and lifecycle. Every entity type may declare a lifecycle as an explicit state machine, making valid states and transitions part of the schema rather than the documentation. Dependencies between lifecycles are declared as coupling rules.

The conceptual vocabulary Eidos uses for this — entities, relationships, states, roles, mixins — draws on the Unified Foundational Ontology (UFO, Guizzardi et al.), which provides a well-studied grounding for these ideas. But the chosen the terminology is intentionally more HTTP API centric. Familiarity with UFO is not required to use Eidos; the concepts are explained on their own terms in §2. For a the reader who is familiar, the mapping is roughly kind→entity, relator→relationship, relatum→participant, phase→state. 


> **Name:** _Eidos_ is a Greek word (εἶδος) meaning form or essence — the idea that a type is defined by what it essentially is, including how it comes to be, what it can become, and how it relates to other things. It was chosen as an unobtrusive name, not a statement.

---

## 2 Core Concepts

Eidos uses a small set of precisely defined concepts. Where a concept corresponds to a UFO term, that mapping is noted — but familiarity with UFO is not required.

| Concept        | Definition                                                                                                                                                                                                                                                    |
| -------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `endurant`     | An instance of an entity or a relationship — any typed thing that has identity and may have a lifecycle. Endurants are wholly present at any instant; they are not extended in time. Corresponds to _endurant_ in UFO.                                        |
| `entity`       | A substantial type whose instances are endurants with their own identity and lifecycle. Maps to a primary REST resource (e.g., Person, Contract, Invoice). Corresponds to _kind_ in UFO.                                                                      |
| `relationship` | A reified connection between two or more entities. A relationship instance is itself an endurant with its own identity, URL, properties, and lifecycle. Maps to a top-level REST resource (e.g., Employment, ContractParty). Corresponds to _relator_ in UFO. |
| `state`        | An intrinsic lifecycle state of an entity or relationship. An endurant satisfies certain natural conditions when in a given state. States form the nodes of the state machine.                                                                                |
| `role`         | An extrinsic classification an entity acquires by participating in a relationship. Roles may add properties and constraints visible in the context of that participation (e.g., Employee, Employer).                                                          |
| `mixin`        | A cross-cutting set of properties and/or lifecycle fragments applicable to multiple entities or relationships (e.g., Auditable, SoftDeletable).                                                                                                               |
| `archetype`    | A named, reusable state machine template. Defined in a library file, referenced by entities or relationships in schema files. Archetypes are themselves written in Eidos.                                                                                     |
| `transition`   | A directed edge in the state machine: a named event that moves an endurant from one state to another, optionally subject to a guard condition.                                                                                                                |
| `guard`        | A predicate that must hold for a transition to be permitted. Guards may reference intrinsic properties, relationship states, or role membership.                                                                                                              |

### 2.1 Relationships as Endurants

In Eidos, a connection between two entities is not a field — it is a `relationship`, a first-class endurant with its own URL, identity, and lifecycle. For example:

```
relationship Employment between
  employee : Person       [ role: Employee ],
  employer : Organization [ role: Employer ] {

  properties {
    startDate : Date     [ required ]
    title     : String
    salary    : Money
  }

  lifecycle: Employable
}
```

This produces a top-level REST resource at `/employments/{id}`, with projection collection endpoints on both participants:

- `GET /employments/{id}` — canonical representation
- `GET /persons/{id}/employments` — filtered to Employments where person plays Employee
- `GET /organizations/{id}/employments` — filtered to Employments where org plays Employer

Every representation — whether returned from the canonical endpoint or a projection — includes a `_self` link pointing to the canonical URL, so clients never need to construct it:

```json
{
  "_type": "Employment",
  "_self": "/employments/42",
  "_state": "Active",
  "employee": "/persons/7",
  "employer": "/organizations/3",
  "startDate": "2023-01-15",
  "title": "Senior Engineer",
  "salary": { "amount": 120000, "currency": "USD" }
}
```

The `_self`, `_type`, and `_state` fields are reserved system properties emitted by the compiler on every generated schema. Designers do not declare them.

By default, participants are represented as URLs. An `?expand` query parameter is generated for every relationship endpoint, allowing the client to request inline embedding of one or more participants instead:

```http
GET /employments/42?expand=employee,employer
```

```json
{
  "_type": "Employment",
  "_self": "/employments/42",
  "_state": "Active",
  "employee": {
    "_type": "Person",
    "_self": "/persons/7",
    "_state": "Active",
    "name": "Ada Lovelace",
    "email": "ada@example.com"
  },
  "employer": {
    "_type": "Organization",
    "_self": "/organizations/3",
    "_state": "Active",
    "legalName": "Analytical Engines Ltd."
  },
  "startDate": "2023-01-15",
  "title": "Senior Engineer",
  "salary": { "amount": 120000, "currency": "USD" }
}
```

The `expand` parameter accepts a comma-separated list of participant names as declared in the `between` clause. Requesting an unknown participant name is a `400 Bad Request`.

### 2.2 Lifecycle as a State Machine

Every entity and relationship may declare a state machine, either inline or by referencing a named archetype. The state machine is a directed graph of states connected by named transitions.

State transitions and property updates are deliberately separated into two distinct operations:

- **`PATCH /{id}`** — property update, expressed as a JSON Patch (RFC 6902) document with media type `application/json-patch+json`. The body is a sequence of operations (`add`, `remove`, `replace`, …) over the resource's properties. Never changes the state: operations targeting the reserved fields (`_self`, `_type`, `_state`) or a property not writable in the current state are rejected with `400 Bad Request`.
- **`PUT /{id}/_state`** — lifecycle transition. Body declares the target state and, when required, the transition name.

This separation is unambiguous at the routing level and makes the intent explicit to API clients. A JSON Patch request that replaces a property looks like:

```http
PATCH /persons/42
Content-Type: application/json-patch+json

[ { "op": "replace", "path": "/email", "value": "ada@new.example.com" } ]
```

The `PUT /_state` request body is:

```json
{ "state": "<TargetState>" }
```

When the target state is reachable via multiple transitions from the current state, the `transition` field is additionally required:

```json
{ "state": "<TargetState>", "transition": "<TransitionName>" }
```

The compiler determines whether `transition` is required by analyzing the state machine. No additional declaration by the designer is needed — transition names are already declared in the `transitions` block and serve directly as the discriminating values. The compiler generates the OpenAPI schema for `PUT /_state` accordingly, marking `transition` as conditionally required.

`PUT /_state` is idempotent: sending the same request when the endurant is already in the target state returns `409 Conflict`. The server state is unchanged, satisfying the idempotency guarantee.

Determinism is a design quality that Eidos encourages — it makes the `PUT /_state` body simpler for clients and the intent clearer for designers. The compiler analyzes every state machine and reports its classification:

- **Deterministic** — every (source, target) pair is unique. `PUT /_state` body requires only `state`. The transition is inferred unambiguously by the server.
- **Non-deterministic** — one or more (source, target) pairs are shared by multiple transitions. `PUT /_state` body requires both `state` and `transition` for the affected target states. The compiler emits a warning suggesting the designer consider restructuring.
- **Ambiguous (error)** — one or more (source, target) pairs are shared by transitions with identical names, making the `transition` field useless as a discriminator. The compiler reports an error and produces no output until the designer resolves the naming conflict.

```
// Deterministic — same transition name, different source states
transitions {
  confirm   : Probationary -> Active
  terminate : Active       -> Terminated
  terminate : OnLeave      -> Terminated
}
```

```http
PUT /employments/42/_state
{ "state": "Terminated" }
```

```
// Non-deterministic — two named transitions from the same source to the same target
transitions {
  resign  : Active -> Terminated
  dismiss : Active -> Terminated
}
```

```http
PUT /employments/42/_state
{ "state": "Terminated", "transition": "resign" }
```

### 2.3 Archetype Library

Writing full state machines inline is verbose. Eidos provides an **archetype** mechanism: named, reusable state machine templates defined in separate `.eidos` files and referenced by name in schema files.

Built-in archetypes cover common patterns. User-defined archetypes use the same syntax and can be added to a project's archetype library.

| Archetype       | Description                                                                                       |
| --------------- | ------------------------------------------------------------------------------------------------- |
| `Immutable`     | No mutations after creation. No PATCH or DELETE generated.                                        |
| `WriteOnce`     | Properties settable on creation only; all become read-only thereafter.                            |
| `ReadOnly`      | Externally managed. GET only.                                                                     |
| `SoftDeletable` | Adds Deleted state; DELETE marks rather than removes. Excluded from default collection responses. |
| `Disableable`   | Adds Enabled / Disabled toggle via `PUT /_state`.                                                 |
| `Publishable`   | Draft → Published → Archived.                                                                     |
| `Approvable`    | Draft → UnderReview → Approved \| Rejected → (Rejected → Draft).                                  |
| `Activatable`   | Inactive ↔ Active (bidirectional toggle).                                                         |

> **Design note:** Archetype composition (e.g., `Disableable + SoftDeletable`) requires merging two state machines into a product automaton. This is supported only for archetypes that declare themselves `composable` and define compatible state namespaces. See §5.3 (TBD).

---

## 3 Language Structure

### 3.1 File Types

All `.eidos` files use the same syntax and may freely mix `entity`, `relationship`, `mixin`, and `archetype` declarations within a single file. There is no enforced separation at the file level.

The constraint applies at the **import boundary**: a file may only be imported by another file if it contains exclusively `archetype` declarations. This allows archetype libraries to be distributed and versioned independently, without pulling in schema-level entity or relationship definitions as a side effect.

A file that mixes `archetype` declarations with `entity` or `relationship` declarations is a valid schema file but is not importable as a library. The compiler enforces this: an attempt to import such a file produces an error.

In practice this leads to a natural convention — archetype libraries live in dedicated files, schemas live in separate files — without mandating it.

### 3.2 Namespace and Import

> ⬜ **TBD:** Namespace declaration syntax and import/use directives for archetype libraries and cross-file entity references. Consider whether namespaces map to URL path prefixes.

### 3.3 Top-level Declarations

A `.eidos` file may contain zero or more of the following top-level declarations, in any order:

- `entity` — a substantial endurant type
- `relationship` — a reified connection between entities, with its own identity and lifecycle
- `mixin` — a cross-cutting property/lifecycle fragment
- `archetype` — a named state machine template

A file containing only `archetype` declarations is importable as a library. See §3.1.

---

## 4 Grammar (ABNF)

The following grammar is expressed in Augmented Backus-Naur Form (RFC 5234). Terminal strings are case-insensitive unless noted. Whitespace (SP, HTAB, CRLF, LF) is permitted between any two tokens and is not shown explicitly in most rules.

### 4.1 Lexical Rules

```abnf
; ── character classes ──────────────────────────────────────────
ALPHA       =  %x41-5A / %x61-7A        ; A-Z / a-z
DIGIT       =  %x30-39                  ; 0-9
UNDERSCORE  =  %x5F                     ; _
HYPHEN      =  %x2D                     ; -

; ── identifiers ─────────────────────────────────────────────────
; TypeName: PascalCase, starts with uppercase
TypeName    =  ALPHA *( ALPHA / DIGIT )  ; first char uppercase by convention

; name: camelCase or snake_case identifier
name        =  ( ALPHA / UNDERSCORE )
               *( ALPHA / DIGIT / UNDERSCORE / HYPHEN )

; ── literals ────────────────────────────────────────────────────
string-lit  =  DQUOTE *( %x20-21 / %x23-7E ) DQUOTE
int-lit     =  1*DIGIT

; ── comments ────────────────────────────────────────────────────
comment      =  line-comment / block-comment
line-comment =  "//" *( %x09 / %x20-7E ) ( CRLF / LF )
block-comment=  "/*" *( %x09 / %x0A / %x0D / %x20-7E ) "*/"
```

### 4.2 Top-level Document

```abnf
document    =  *( ws / comment / top-decl )

top-decl    =  entity-decl
            /  relationship-decl
            /  mixin-decl
            /  archetype-decl

ws          =  1*( SP / HTAB / CRLF / LF )
```

### 4.3 Entity Declaration

```abnf
entity-decl   =  [annotation] "entity" TypeName
               [ "with" mixin-list ]
               "{" entity-body "}"

mixin-list  =  TypeName *( "," TypeName )

entity-body   =  *( ws / comment / entity-member )

entity-member =  doc-comment
            /  properties-block
            /  lifecycle-clause
            /  url-hint

; Optional designer hint to override generated URL path segment
url-hint    =  "url" ":" string-lit
```

### 4.4 Relationship Declaration

```abnf
relationship-decl =  [annotation] "relationship" TypeName
                "between" participant-list
                "{" relationship-body "}"

participant-list =  participant "," participant *( "," participant )

participant  =  name ":" TypeName
                [ "[" participant-opts "]" ]

participant-opts =  participant-opt *( "," participant-opt )
participant-opt  =  "role" ":" TypeName
                 /  "multiplicity" ":" multiplicity

multiplicity =  "one" / "many" / "one-or-more" / "zero-or-one"

relationship-body =  *( ws / comment / relationship-member )

relationship-member = doc-comment
              /  properties-block
              /  lifecycle-clause
              /  coupling-block
              /  role-decl
              /  url-hint
```

### 4.5 Properties Block

```abnf
properties-block =  "properties" "{" *( ws / comment / property-decl ) "}"

property-decl    =  [annotation] name ":" type-expr
                    [ "[" prop-opts "]" ]

type-expr        =  scalar-type
                 /  "ref" TypeName          ; reference to another entity
                 /  "list" "<" type-expr ">"
                 /  "optional" "<" type-expr ">"
                 /  "enum" "[" enum-values "]"

scalar-type      =  "String" / "Integer" / "Number" / "Boolean"
                 /  "Date" / "DateTime" / "Time"
                 /  "Money" / "Email" / "Url" / "Uuid"

enum-values      =  name *( "," name )

prop-opts        =  prop-opt *( "," prop-opt )
prop-opt         =  "required"
                 /  "unique"
                 /  "readonly"
                 /  "writeonce"
                 /  "immutable"           ; alias for writeonce
                 /  "nullable"
                 /  visibility-opt
                 /  state-visibility

; Property visible only in given states
state-visibility =  "visible-in" ":" "[" TypeName *( "," TypeName ) "]"

; Property writable only in given states
visibility-opt   =  "writable-in" ":" "[" TypeName *( "," TypeName ) "]"
```

### 4.6 Lifecycle Clause

```abnf
; An endurant uses either a named archetype OR an inline state machine, not both.

lifecycle-clause =  "lifecycle" ":" archetype-ref
                 /  "lifecycle" "{" inline-lifecycle "}"

archetype-ref    =  TypeName *( "+" TypeName )    ; composition (§5.3 TBD)

inline-lifecycle =  *( ws / comment / lifecycle-member )

lifecycle-member =  states-block
                 /  transitions-block
                 /  initial-clause

initial-clause   =  "initial" ":" TypeName

states-block     =  "states" "{" *( ws / comment / state-decl ) "}"

state-decl       =  [annotation] TypeName [ string-lit ]  ; optional doc label

transitions-block=  "transitions" "{" *( ws / comment / transition-decl ) "}"

transition-decl  =  [annotation] name ":"
                    state-set "->" TypeName
                    [ "[" transition-opts "]" ]

; Source may be a single state or a set
state-set        =  TypeName / "(" TypeName 1*( "|" TypeName ) ")"

transition-opts  =  transition-opt *( "," transition-opt )
transition-opt   =  "guard" ":" guard-expr
                 /  "emits" ":" TypeName     ; future: event name

guard-expr       =  guard-atom *( ( "&&" / "||" ) guard-atom )
guard-atom       =  name                    ; predicate name (resolved externally)
                 /  participant-state-guard
                 /  "!" guard-atom
                 /  "(" guard-expr ")"

participant-state-guard = name ".state" "in" "[" TypeName *( "," TypeName ) "]"
```

### 4.7 Coupling Block

Coupling rules declare how a relationship's lifecycle reacts to state changes in its participants. This is what allows "when Organization transitions to Dissolved, terminate all its Employments" to be a first-class declaration rather than application logic.

```abnf
coupling-block   =  "coupling" "{" *( ws / comment / coupling-rule ) "}"

coupling-rule    =  "on" name ".state" "->" TypeName ":" transition-call

; The transition to invoke on this relationship when the coupling fires
transition-call  =  name [ "(" coupling-args ")" ]

coupling-args    =  coupling-arg *( "," coupling-arg )
coupling-arg     =  name ":" ( name / string-lit )

; Examples:
;   on employer.state -> Dissolved : terminate
;   on employee.state -> Deceased  : terminate( reason: natural-end )
```

### 4.8 Mixin Declaration

```abnf
mixin-decl       =  [annotation] "mixin" TypeName "{" mixin-body "}"

mixin-body       =  *( ws / comment / mixin-member )

mixin-member     =  doc-comment
                 /  properties-block
                 /  lifecycle-fragment   ; partial state machine — TBD §7.9
```

### 4.9 Archetype Declaration

```abnf
archetype-decl   =  [annotation] "archetype" TypeName
                    [ "composable" ]       ; opt-in to + composition
                    "{" inline-lifecycle "}"

; Archetypes use the same inline-lifecycle syntax as entities and relationships.
; They may additionally declare which states are 'open' for composition.
```

> **Note:** Archetypes use the same `inline-lifecycle` syntax as entities and relationships. A file containing only `archetype` declarations is importable as a library (see §3.1); a file that mixes `archetype` with `entity` or `relationship` declarations is a valid schema file but cannot be imported.

### 4.10 Role Declaration

Roles are declared inline in the participant list of a relationship and may be elaborated in a `role` block within the relationship body.

```abnf
role-decl        =  "role" TypeName "{" role-body "}"

role-body        =  *( ws / comment / role-member )

role-member      =  doc-comment
                 /  properties-block
                 /  guard-constraint

; A constraint that must hold for the entity to be eligible to play this Role
guard-constraint =  "requires" ":" guard-expr
```

### 4.11 Annotations and Doc Comments

```abnf
annotation       =  "@" name [ "(" annotation-args ")" ]

annotation-args  =  annotation-arg *( "," annotation-arg )
annotation-arg   =  name ":" ( string-lit / name / int-lit )

doc-comment      =  '"""' *( %x09 / %x0A / %x0D / %x20-7E ) '"""'

; Built-in annotations:
;   @deprecated(since: "v2", use: "NewType")
;   @since(version: "1.3")
;   @tag(name: "billing")     maps to OpenAPI tags
```

---

## 5 Compiler Semantics

### 5.1 Determinism Analysis

After parsing, the compiler performs a determinism analysis on each state machine (inline or resolved from an archetype). For each state P, it collects all outgoing transitions and checks whether any two share the same target state.

The compiler classifies the machine and reports the result to the designer:

- **Deterministic** — every (source, target) pair maps to exactly one transition name. The server can infer the transition unambiguously from current state and target state alone. `PUT /_state` body requires only `{ "state": "<target>" }`.
- **Non-deterministic** — one or more (source, target) pairs are shared by transitions with distinct names. The `transition` field is required in the `PUT /_state` body for the affected target states. The compiler emits a warning and generates the OpenAPI schema with `transition` conditionally required.
- **Ambiguous (error)** — one or more (source, target) pairs are shared by transitions that have identical names, making `transition` useless as a disambiguator. The compiler reports an error and produces no output until the designer renames the conflicting transitions.

Determinism is not required — but it is preferred. The compiler may suggest restructuring when a non-deterministic machine could be made deterministic without loss of expressiveness (e.g., by choosing more specific transition names or splitting a source state).

### 5.2 HTTP Surface Generation

From a valid Eidos schema the compiler generates an OpenAPI 3.x document. The mapping rules are:

| Eidos construct                        | Generated HTTP surface                                                                                                                            |
| -------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------- |
| `entity E`                             | `GET /es`, `POST /es`, `GET /es/{id}`, `PATCH /es/{id}`, `PUT /es/{id}/_state`, `DELETE /es/{id}` (subject to lifecycle)                          |
| `relationship R`                       | `GET /rs`, `POST /rs`, `GET /rs/{id}`, `PATCH /rs/{id}`, `PUT /rs/{id}/_state`, `DELETE /rs/{id}` plus projection collections on each participant |
| Every representation                   | Always includes reserved fields `_self` (canonical URL), `_type` (TypeName), `_state` (current state). Designers do not declare these.            |
| `relationship R` — `?expand`           | `GET /rs/{id}?expand=<participant>[,<participant>]` embeds inline representations of the named participants instead of URLs                       |
| `PATCH /{id}`                          | Property update via JSON Patch (RFC 6902), media type `application/json-patch+json`; body is an operations array. Never changes the state — ops on reserved fields or properties not writable in the current state are rejected (`400`). |
| `PUT /{id}/_state` — deterministic     | Body: `{ "state": "<TargetState>" }`. Transition inferred by server from current state and target state.                                          |
| `PUT /{id}/_state` — non-deterministic | Body: `{ "state": "<TargetState>", "transition": "<TransitionName>" }`. `transition` is conditionally required per OpenAPI schema.                |
| `Immutable` archetype                  | No PATCH, PUT, or DELETE operations generated                                                                                                     |
| `ReadOnly` archetype                   | GET only; no POST, PATCH, PUT, DELETE                                                                                                             |
| `SoftDeletable` archetype              | DELETE sets state to Deleted; `GET /es` excludes Deleted by default; `GET /es?include=deleted` available                                          |
| `state-visibility`                     | Properties only appear in response schemas for the listed states                                                                                  |
| `writable-in`                          | A property is patchable only in the listed states; a JSON Patch op targeting it outside those states is rejected (`400`)                          |
| `coupling` rules                       | Documented in operation descriptions via `x-eidos-coupling` extensions; enforced server-side                                                      |

### 5.3 Archetype Composition

> ⬜ **TBD:** Rules for combining two or more `composable` archetypes via the `+` operator. Likely requires: disjoint state namespaces, declared merge points, product automaton construction. Open question: whether to support this at v1 or defer.
>
> A concrete illustration of the difficulty: `ReadOnly` and `Immutable` have no states and no transitions — they are purely HTTP surface constraints rather than lifecycle shapes. Composing either with an archetype that _does_ have states (e.g., `ReadOnly + Activatable`) produces a logical contradiction: the resulting endurant would have states `Inactive` and `Active` in the model, yet the HTTP surface would expose no way to change state. This suggests that state-free archetypes should be marked explicitly non-composable, and that the `composable` keyword (and the `+` operator) should be restricted to archetypes with at least one state. The deeper open question is whether composition is fundamentally a product of _lifecycles_ only, with HTTP surface constraints always derived separately.

### 5.4 Guard Resolution

> ⬜ **TBD:** Guards reference named predicates. The compiler emits guard names into OpenAPI operation descriptions (`x-eidos-guard` extensions). Resolution of guard predicates to server-side code is outside the language scope but should be documented here with integration patterns.

---

## 6 Complete Example

### 6.1 Archetype file: `hr-archetypes.eidos`

```
archetype Employable composable {
  initial: Probationary

  states {
    Probationary  "On trial period"
    Active        "Fully employed"
    OnLeave       "Approved leave of absence"
    Terminated    "Employment ended"
  }

  transitions {
    confirm    : Probationary -> Active
    grantLeave : Active       -> OnLeave
    return     : OnLeave      -> Active
    terminate  : ( Active | OnLeave | Probationary ) -> Terminated
  }
}
```

### 6.2 Schema file: `hr.eidos`

```
mixin Auditable {
  properties {
    createdAt  : DateTime  [ readonly ]
    modifiedAt : DateTime  [ readonly ]
    createdBy  : ref Person [ readonly ]
  }
}

entity Person with Auditable {
  """A natural person known to the system."""

  properties {
    name   : String  [ required ]
    email  : Email   [ required, unique ]
    taxId  : String  [ writeonce ]
  }

  lifecycle {
    initial: Applicant

    states {
      Applicant
      Active
      Suspended
      Deceased
    }

    transitions {
      onboard   : Applicant              -> Active     [ guard: taxIdVerified ]
      suspend   : Active                 -> Suspended
      reinstate : Suspended              -> Active
      close     : ( Active | Suspended ) -> Deceased
    }
  }
}

entity Organization with Auditable {
  properties {
    legalName : String [ required ]
    vatNumber : String
  }

  lifecycle: Activatable
}

relationship Employment between
  employee : Person       [ role: Employee ],
  employer : Organization [ role: Employer ] {

  """Mediates the employment relationship between a Person and an Organization."""

  properties {
    startDate : Date   [ required ]
    endDate   : Date   [ nullable ]
    title     : String
    salary    : Money
  }

  lifecycle: Employable

  coupling {
    on employer.state -> Dissolved : terminate
    on employee.state -> Deceased  : terminate
  }

  role Employee {
    properties {
      employeeId  : String [ required ]
      department  : String
    }
  }
}
```

### 6.3 Generated HTTP surface (excerpt)

```
GET    /employments               -> 200 list<Employment>
POST   /employments               -> 201 Employment  (initial state: Probationary)
GET    /employments/{id}          -> 200 Employment
         response always includes _self, _type, _state
GET    /employments/{id}?expand=employee,employer
                                  -> 200 Employment  (participants embedded inline)

PATCH  /employments/{id}          -> 200 Employment  (JSON Patch; property update only)
         Content-Type: application/json-patch+json
         body: [ { "op": "replace", "path": "/title", "value": "Lead Engineer" } ]

PUT    /employments/{id}/_state   -> 200 Employment  (lifecycle transition)
         deterministic body:     { "state": "Active" }
         deterministic body:     { "state": "Terminated" }
         non-deterministic body: { "state": "Terminated", "transition": "resign" }
                              or { "state": "Terminated", "transition": "dismiss" }

DELETE /employments/{id}          -> 405 Method Not Allowed  (Employable has no delete transition)

GET    /persons/{id}/employments        -> 200 list<Employment>  (employee projection)
                                           each item includes _self pointing to /employments/{id}
GET    /organizations/{id}/employments  -> 200 list<Employment>  (employer projection)
                                           each item includes _self pointing to /employments/{id}

GET    /persons/{id}              -> 200 Person
PATCH  /persons/{id}              -> 200 Person  (JSON Patch; property update only)
         Content-Type: application/json-patch+json
         body: [ { "op": "replace", "path": "/email", "value": "ada@new.example.com" } ]
PUT    /persons/{id}/_state       -> 200 Person
         body: { "state": "Active" }      (guard taxIdVerified enforced server-side)
         body: { "state": "Suspended" }
         body: { "state": "Deceased" }
```

---

## 7 Open Design Questions

The following items are deferred for later revisions of this specification.

**7.1 Archetype composition (§5.3)**
Product automaton rules; which built-in archetypes are declared composable; whether composition is supported in v1.

**7.2 Namespace and import**
Whether namespaces map to URL path prefixes. Cross-file entity references. Distribution of archetype libraries as packages.

**7.3 Guard predicate language**
Whether guards are just names resolved externally, or whether Eidos provides a limited expression language for structural guards (e.g., property comparisons, role membership tests).

**7.4 Event emission**
The `transition-opt` `emits: TypeName` is reserved. Full event catalog generation (AsyncAPI or CloudEvents schema) is a planned extension.

**7.5 Transition history resource**
Auto-generating `GET /{entity}/{id}/history` as an opt-in (possibly via a `History` mixin or archetype annotation).

**7.6 Pagination, filtering, sorting**
Standard query parameter conventions for collection endpoints. Whether Eidos should opine on these or leave them to OpenAPI extensions.

**7.7 Versioning**
How Eidos handles API versioning (URL path versioning vs. header-based). Whether version is a top-level schema attribute.

**7.8 Security and authorization**
Whether role/state combinations map to OAuth scopes or other access control declarations.

**7.9 Mixin lifecycle fragments**
Whether a Mixin may contribute partial state machine fragments to any entity or relationship that includes it (e.g., `SoftDeletable` as a Mixin rather than an archetype).

---

## 8 Relationship to Existing Specifications

| Specification | Relationship                                                                                                                                                                     |
| ------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| OpenAPI 3.x   | Eidos compiles to OpenAPI. Eidos is not a superset of OpenAPI; it operates at a higher level of abstraction.                                                                     |
| JSON Schema   | Property `type-expr`s map to JSON Schema. Eidos scalar types have defined JSON Schema equivalents.                                                                               |
| UFO           | Eidos uses a pragmatic subset of UFO: entity (kind), relationship (relator), state, role, mixin. It does not claim to be a full UFO implementation.                              |
| OWL / RDF     | Not a target. Eidos is an API design language, not a knowledge representation language, though the concepts are compatible.                                                      |
| AsyncAPI      | Planned future target for event emission (§7.4).                                                                                                                                 |
| CMMN          | Case Management Model and Notation covers similar lifecycle territory. Eidos is narrower (HTTP APIs) and does not model case workers or ad-hoc tasks.                            |
| OData         | OData defines entity relationships as navigation properties. Eidos's Relators are richer (own lifecycle, own URL), but an OData backend could be generated from an Eidos schema. |

---

## Appendix A — Built-in Archetype Definitions

The following are the normative definitions of Eidos's built-in archetypes, expressed in Eidos syntax.

### A.1 Immutable

```
archetype Immutable {
  // No states, no transitions.
  // Compiler generates POST only. No PATCH, no DELETE.
}
```

### A.2 SoftDeletable

```
archetype SoftDeletable composable {
  initial: Active

  states {
    Active
    Deleted
  }

  transitions {
    delete  : Active  -> Deleted
    restore : Deleted -> Active
  }
}
```

### A.3 Publishable

```
archetype Publishable {
  initial: Draft

  states {
    Draft
    UnderReview
    Published
    Archived
  }

  transitions {
    submit    : Draft       -> UnderReview
    publish   : UnderReview -> Published
    reject    : UnderReview -> Draft
    archive   : Published   -> Archived
    unarchive : Archived    -> Published
  }
}
```

### A.4 Approvable

```
archetype Approvable {
  initial: Draft

  states {
    Draft
    UnderReview
    Approved
    Rejected
  }

  transitions {
    submit  : Draft       -> UnderReview
    approve : UnderReview -> Approved
    reject  : UnderReview -> Rejected
    revise  : Rejected    -> Draft
  }
}
```

### A.5 Activatable

```
archetype Activatable composable {
  initial: Inactive

  states {
    Inactive
    Active
  }

  transitions {
    activate   : Inactive -> Active
    deactivate : Active   -> Inactive
  }
}
```

### A.6 Disableable

```
archetype Disableable composable {
  initial: Enabled

  states {
    Enabled
    Disabled
  }

  transitions {
    disable : Enabled  -> Disabled
    enable  : Disabled -> Enabled
  }
}
```

### A.7 WriteOnce

```
archetype WriteOnce {
  // No states, no transitions.
  // All properties become read-only after the endurant is created.
  // Compiler generates POST and GET only.
  // Not composable — see §5.3.
}
```

### A.8 ReadOnly

```
archetype ReadOnly {
  // No states, no transitions.
  // Externally managed endurant. Compiler generates GET only.
  // Not composable — see §5.3.
}
```
