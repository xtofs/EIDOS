# Create/Update require explicit type arguments — OPEN DESIGN QUESTION

**Status: open / undecided.** This is not a bug; it's a deliberate-for-now wart with a real trade-off to settle.

## Symptom

With the forced `Response<T>` builder, the two-type-parameter verbs need both type args spelled out at the
call site:

```csharp
.Create<PersonCreateRequest, PersonDto>(CreatePerson)
.Update<JsonPatchDocument<PersonPatch>, PersonDto>(UpdatePerson)
```

`List` / `Get` / `Transition` / `GetEntity` infer fine (their type parameter is only in the delegate's
*return*); only `Create<TRequest, T>` and `Update<TRequest, T>` are affected.

## Why it can't be inferred (so we don't re-litigate)

- A **method group** (e.g. `CreatePerson`) provides inference only from the delegate's **return** type, and
  only once the delegate's **parameter** types are already fixed. It never feeds inference from a parameter
  position. `TRequest` lives only in the parameter position → unfixable → `T` is blocked too → CS0411.
- **Generic constraints are never an inference source** (`where TRequest : CreateRequest<T>` does not let `T`
  fall out of `TRequest`). Constraints are validated after inference, not used to drive it.
- Base-class/phantom-type inference (`CreateRequest<T>`) only works from a **concrete argument value**
  (`M(new PersonCreateRequest())`), not from a handler method group whose request type sits in the parameter
  slot. (And a base-typed delegate parameter wouldn't accept the derived-typed handler anyway — delegate
  parameters are contravariant.)

## Options (pick one later)

1. **Status quo** — keep the explicit `<TRequest, T>` args. Most type-safe, slightly verbose.
2. **Explicitly-typed lambda** at the call site — `.Create((PersonCreateRequest r) => CreatePerson(r))` infers
   both, but is more clutter than just writing the type args.
3. **Drop `T` as a type parameter.** The framework does **not** need it: `RepresentationWriter` serializes via
   `value.GetType()` at runtime, so `T` exists only for the handler author's return-type safety. A non-generic
   `Response` (with `Response<T> : Response` for ergonomic construction) would make `Create<TRequest>(
   Func<TRequest, Task<Response>>)` need just **one** explicit arg. Trade-off: the handler's signature would no
   longer state its response type, and there are `Task<T>` invariance snags when widening `Response<T>` to the
   base in the delegate's return position to work through.

No decision yet — revisit when the builder API is next touched.
