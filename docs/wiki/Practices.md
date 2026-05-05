# Practices

This page focuses on *why* certain shapes work well with this library, and when you might choose alternatives.

## Practice 1: Prefer 1:1 request models (when it fits)

If your “command” shape matches your HTTP contract, bind it directly.

### Why this is a great default here

- **Less glue code**: no mapping/adapters that can drift over time.
- **Fewer failure modes**: fewer places to forget a field or apply the wrong conversion.
- **Immutable-friendly**: ctor-based creation for `record` / `record struct` fits “data in → command out”.
- **Performance clarity**: the generated binder is the whole story (no hidden runtime resolution).

### When 1:1 becomes painful (and a transport DTO is worth it)

Use a separate transport DTO only when you actually need a boundary:

- **Contract churn / versioning** (v1/v2 payload shapes, legacy keys)
- **Multiple HTTP contracts** for the same domain action (different endpoints/clients)
- **HTTP-only concerns** you don’t want in the domain model (cookies/headers/security metadata)
- **PATCH / partial updates** where “missing vs default” must be represented explicitly

## Practice 2: Keep the “update envelope” idea, but stay concrete for HTTP

A common pattern is an update envelope:

- route provides the target id
- body provides the payload

You *can* express it as a generic:

```csharp
public readonly record struct UpdateCommand<TPayload>(
    [property: FromRoute("entityId")] int TargetId,
    [property: FromBody] TPayload Payload);
```

However, source generation works best when the request model is **concrete**.
Open generic request types make binder naming/emission and DI registration harder.

**Recommended in practice:** keep the envelope concept, but define a concrete request type per HTTP contract:

```csharp
public readonly record struct UpdateSomeEntityCommand(
    [property: FromRoute("entityId")] int TargetId,
    [property: FromBody] UpdateSomeEntityPayload Payload);
```

This keeps the request model 1:1 and generation-friendly.

## Practice 3: Make body shape explicit when it buys you something

Wrapping the body into a dedicated type can be useful:

```csharp
public readonly record struct UpdateSomeEntityCommand(
    [property: FromRoute("entityId")] int TargetId,
    [property: FromBody] UpdateSomeEntityBody Body)
{
    public UpdateSomeEntityPayload Payload => Body.Payload;
}

public sealed record UpdateSomeEntityBody(UpdateSomeEntityPayload Payload);
```

### Why you might do this

- Leaves room for **metadata** later (`traceId`, `version`, `meta`, etc.) without breaking the route/query shape.
- Avoids naming collisions and keeps “route/query vs body” visually separated.

### Why you might not

- It changes the JSON shape (you now expect `{"body":{...}}` unless your body parser is configured otherwise).
- Adds an extra type (still no mapping, but more declarations).

## Practice 4: Keep request models immutable

Prefer immutable request models:

- `record` / `record struct`
- ctor parameters annotated with `[property: FromX]`

Why:

- the binder can create the request in one shot (no partial mutation)
- it prevents half-initialized objects
- it matches “command” semantics

