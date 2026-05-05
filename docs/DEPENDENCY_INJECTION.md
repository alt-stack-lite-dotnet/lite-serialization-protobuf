# Dependency Injection (repo)

## Hot path rule

Generated binders must not do runtime resolution in the hot path (e.g. `request.HttpContext.RequestServices.GetService(...)`).

Instead, the generator emits binder constructors with the required dependencies, and your app registers binders as singletons.

## Body parsing

`IBodyParser` is mandatory for `[FromBody]`.

Implementations (e.g. `SystemTextJsonBodyParser` from `Lite.Serialization.Protobuf.Body.Json`) use request body buffering and rewind so the body stream can be read as needed; they expose `TryParseAsync` and return `(Success, Value)` instead of throwing on parse errors.

