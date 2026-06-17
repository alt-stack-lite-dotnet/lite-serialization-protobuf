# CLI — `lite-proto`

A small tool for moving between C# and `.proto`, both directions.

```
lite-proto export   Extract .proto schemas embedded in a built assembly into .proto files
lite-proto import   Generate C# message types from .proto files
```

Run `lite-proto <command> --help` for options.

## `export` — code-first (POCO → .proto)

The source generator embeds a `.proto` schema for every serialized type as an assembly attribute
(`[GeneratedProtoSchema]`). `export` reads those out of a built DLL (via `MetadataLoadContext` — the
assembly is **not** executed) and writes `.proto` files.

```bash
lite-proto export path/to/MyApp.dll --output ./proto
```

| Option | Meaning |
|---|---|
| `<assembly.dll>` | the built assembly to read (required) |
| `-o, --output <dir>` | output directory (default: current) |
| `-v, --verbose` | log each file written |

Use this to hand a `.proto` to another language/team while keeping C# as the source of truth.

## `import` — proto-first (.proto → C#)

Generates plain POCO message types from `.proto` files. Each message becomes a `class` or `struct`
depending on size (heuristic) and your overrides; enums, `repeated`, and `map` are mapped to
`List<T>` / `Dictionary<K,V>`. Explicit field numbers from the `.proto` are emitted as
`const XxxFieldNumber` so the generated types are wire-compatible.

```bash
lite-proto import api.proto common.proto --namespace MyApp.Contracts --output ./generated
```

| Option | Meaning |
|---|---|
| `<file.proto> …` | one or more `.proto` files (required) |
| `-o, --output <dir>` | output directory (default: current) |
| `-n, --namespace <ns>` | C# namespace for generated types |
| `--struct-threshold <n>` | max estimated bytes to emit a `struct` (default: 32) |
| `--all-class` | force every message to a `class` |
| `--all-struct` | force every message to a `struct` |
| `-c, --config <file.json>` | config with per-type overrides |
| `-v, --verbose` | verbose logging |

`--all-class` and `--all-struct` are mutually exclusive.

### Config file

For per-type control:

```json
{
  "namespace": "MyApp.Contracts",
  "structThreshold": 32,
  "forceStruct": ["Point", "Color"],
  "forceClass": ["BigRequest"]
}
```

CLI flags override the matching config values.
