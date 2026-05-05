# Dependency Injection

## Rule: no service resolution in hot path

Generated binders do **not** call `request.HttpContext.RequestServices.GetService(...)` during binding.

All required services (body parser, value parsers, `[FromServices]` dependencies) are injected into the binder constructor.

## Body parsing

`[FromBody]` requires an `IBodyParser`.

Default: `SystemTextJsonBodyParser` (from package `Lite.Serialization.Protobuf.Body.Json`). Body is buffered and rewound (`EnableBuffering()` + `Position = 0`) so multiple reads work; parsers use `TryParseAsync` and do not throw on invalid payloads.

## Value parsing (`IValueParser<T>`)

For route/query/header/cookie/form values, the generator prefers:

1. `IValueParser<T>` (if needed and injected into binder)
2. `IParsable<T>` (when available)
3. built-in primitive parsing

