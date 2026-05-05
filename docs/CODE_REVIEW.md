# Code review (summary)

## Source generator

- **Pipeline**: Attribute-based and config-based discovery are merged via `DedupModels`; attribute model wins when it has more “signal” (BindFromBody or property count). Clear and correct.
- **Naming**: `GetBinderName` with nested types (`Outer_InnerBinder`) and `BinderNameAttribute` override is consistent; wiki Conventions match.
- **Body**: Generated code uses `TryParseAsync<T>` (returns `(bool Success, T? Value)`); one method for both value and reference types. `IBodyParser` and all body parsers implement `TryParseAsync`; parse failures do not throw.
- **Config parsing**: `ParseConfigureBody` only handles `ExpressionStatementSyntax` with `InvocationExpressionSyntax`; chained calls (e.g. `.Bind(x => x.Id).FromRoute("id")`) are walked in `ParseBindingChain`. Logic is sound.
- **Edge case**: `BuildModelFromTypeSymbol` throws if `typeSymbol.Arity > 0`. If a project has `IRequestBindingConfiguration<OpenGeneric<T>>`, the generator will throw. Safer to treat open generics as “no model” (e.g. return null from `TryGetConfig` or catch and skip) so the rest of the project still gets binders.
- **Emit**: Ctor-based creation for record/struct correctly fills locals from route/query/header then from body and builds one ctor call. PropertyBinding uses `PropTypeFqn`; ensure all emit paths use it (already the case).

## Runtime

- **LiteRequestBinder**: Lazy compilation, sync/async split, and “requires async” check are correct.
- **CompiledPropertyBinder**: Body binding uses `TryParseAsync` (non-generic); runtime path sets property only when `Success && value is not null`. Generated binders call `TryParseAsync<T>` and use the value only when success. Fine.
- **Set()**: Fallback for IParsable and built-in types; DI `IValueParser<T>` is used first. No issues.

## Tests

- **UnitTest1**: Covers nested record struct, route + body, generated binder name `BinderGenerationTests_UpdateSomeEntityCommandBinder`. Good.
- **Fakes**: `TestBodyParser` implements `TryParseAsync<T>` (JSON); returns `(true, value)` on success, `(false, default)` on exception. Covers body binding in tests.
- **Coverage**: Adding tests for config-only binding, `BinderNameAttribute` on type and on config, and sync-only binding would strengthen regression safety.

## Docs

- **wiki/Conventions.md**: Accurately describes when a binder is generated and how it is named (default + override). Matches SG behavior.
- **wiki/Practices.md**: 1:1 command, when to add DTO, body shape, immutability — useful and consistent with the design.
- **DEPENDENCY_INJECTION.md / wiki/Dependency-Injection.md**: Align with actual registration (binder vs parser vs value parser) so users are not misled.

## Recommendations

1. **SG**: Done — open generic configs now return null from `BuildModelFromTypeSymbol` / `TryGetConfig` instead of throwing.
2. **Tests**: Add at least one test for config-only model and one for `BinderName("CustomName")` to lock the contract.
3. **CI**: Added `.github/workflows/ci.yml` (restore, build, test on push/PR to main/master; .NET 10.0.x). If the hosted runner does not have .NET 10 yet, switch to `9.0.x` or add a multi-target build.
