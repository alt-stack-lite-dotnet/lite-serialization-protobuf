using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Lite.Serialization.Protobuf.SourceGenerator;

[Generator(LanguageNames.CSharp)]
public sealed class ProtobufSerializationGenerator : IIncrementalGenerator
{
    private const string LiteSerializerFqn = "Lite.Serialization.Protobuf.LiteSerializer";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var callSites = context.SyntaxProvider.CreateSyntaxProvider(
            predicate: static (node, _) => LooksLikeForCall(node),
            transform: static (ctx, ct) => TryExtractCallSite(ctx, ct))
            .Where(static cs => cs is not null)
            .Select(static (cs, _) => cs!.Value);

        var configs = context.SyntaxProvider.CreateSyntaxProvider(
            predicate: static (node, _) => node is ClassDeclarationSyntax { BaseList: not null },
            transform: static (ctx, ct) => TryExtractConfig(ctx, ct))
            .Where(static c => c is not null)
            .Select(static (c, _) => c!);

        // grpc::Marshallers.Create<T>(...) call sites — for the gRPC.NET interop config-toggle
        var marshallerSites = context.SyntaxProvider.CreateSyntaxProvider(
            predicate: static (node, _) => node is InvocationExpressionSyntax inv && IsGenericCall(inv, "Create"),
            transform: static (ctx, ct) => TryExtractMarshallerSite(ctx, ct))
            .Where(static x => x is not null)
            .Select(static (x, _) => x!.Value);

        // build_property.LiteSerializerInterceptGrpc — enables Marshallers.Create interception
        var interceptGrpc = context.AnalyzerConfigOptionsProvider.Select(static (opts, _) =>
            opts.GlobalOptions.TryGetValue("build_property.LiteSerializerInterceptGrpc", out var v)
            && string.Equals(v, "true", StringComparison.OrdinalIgnoreCase));

        var combined = context.CompilationProvider
            .Combine(callSites.Collect())
            .Combine(configs.Collect())
            .Combine(marshallerSites.Collect())
            .Combine(interceptGrpc);

        context.RegisterSourceOutput(combined, static (spc, data) =>
        {
            var compilation = (CSharpCompilation)data.Left.Left.Left.Left;
            var sites = data.Left.Left.Left.Right;
            var configList = data.Left.Left.Right;
            var grpcSites = data.Left.Right;
            var interceptGrpcEnabled = data.Right;
            try { Emit(spc, compilation, sites, configList, interceptGrpcEnabled ? grpcSites : ImmutableArray<GrpcMarshallerSite>.Empty); }
            catch (Exception ex)
            {
                spc.ReportDiagnostic(Diagnostic.Create(GeneratorCrashed, Location.None, ex.GetType().Name, ex.Message));
            }
        });
    }

    // Extracts the invoked method's simple name, whether the call is `Foo(...)`, `Foo<T>(...)`,
    // `x.Foo(...)` or `x.Foo<T>(...)`. Type arguments may be explicit OR inferred — the semantic
    // model in the transform step resolves the real symbol either way.
    private static string? GetInvokedSimpleName(InvocationExpressionSyntax inv) => inv.Expression switch
    {
        IdentifierNameSyntax id => id.Identifier.Text,
        GenericNameSyntax g => g.Identifier.Text,
        MemberAccessExpressionSyntax { Name: { } n } => n.Identifier.Text,
        _ => null,
    };

    private static bool IsGenericCall(InvocationExpressionSyntax inv, string name) =>
        GetInvokedSimpleName(inv) == name;

    private static GrpcMarshallerSite? TryExtractMarshallerSite(GeneratorSyntaxContext ctx, CancellationToken ct)
    {
        var inv = (InvocationExpressionSyntax)ctx.Node;
        if (ctx.SemanticModel.GetSymbolInfo(inv, ct).Symbol is not IMethodSymbol method) return null;
        if (method.ContainingType?.ToDisplayString() != "Grpc.Core.Marshallers") return null;
        if (method.Name != "Create") return null;
        if (method.TypeArguments.Length != 1) return null;
        if (method.TypeArguments[0] is not INamedTypeSymbol target) return null;
        if (target.IsAbstract || target.IsStatic || target.TypeKind == TypeKind.Interface) return null;

        var loc = ctx.SemanticModel.GetInterceptableLocation(inv, ct);
        if (loc is null) return null;

        var paramTypes = string.Join("|", method.Parameters.Select(
            p => p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));

        return new GrpcMarshallerSite(
            target.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            loc.Version, loc.Data, paramTypes);
    }

    // ----- Discovery: LiteSerializer.For/MarshallerFor/Serialize/Deserialize<T>() call sites -----
    private static readonly HashSet<string> InterceptedMethodNames = new(StringComparer.Ordinal)
    {
        "For", "MarshallerFor", "SerializeTo", "DeserializeFrom", "ComputeSize", "SerializeRented",
    };

    private static bool LooksLikeForCall(SyntaxNode node)
    {
        if (node is not InvocationExpressionSyntax inv) return false;
        var name = GetInvokedSimpleName(inv);
        return name is not null && InterceptedMethodNames.Contains(name);
    }

    private static CallSite? TryExtractCallSite(GeneratorSyntaxContext ctx, CancellationToken ct)
    {
        var inv = (InvocationExpressionSyntax)ctx.Node;
        if (ctx.SemanticModel.GetSymbolInfo(inv, ct).Symbol is not IMethodSymbol method) return null;
        if (method.ContainingType?.ToDisplayString() != LiteSerializerFqn) return null;
        if (!InterceptedMethodNames.Contains(method.Name)) return null;
        if (method.TypeArguments.Length != 1) return null;
        if (method.TypeArguments[0] is not INamedTypeSymbol target) return null;
        if (target.IsAbstract || target.IsStatic || target.TypeKind == TypeKind.Interface) return null;

        // Overloaded methods are disambiguated by tagging the resolved method name with a variant.
        string tagName;
        if (method.Name == "SerializeTo")
        {
            // SerializeTo(in T, Span<byte>) -> int   vs   SerializeTo(in T, IBufferWriter<byte>) -> void
            if (method.Parameters.Length != 2) return null;
            tagName = method.Parameters[1].Type.ToDisplayString() switch
            {
                "System.Span<byte>" => "SerializeTo",
                "System.Buffers.IBufferWriter<byte>" => "SerializeToWriter",
                _ => "",
            };
        }
        else if (method.Name == "DeserializeFrom")
        {
            // DeserializeFrom(ReadOnlySpan<byte>) / (ReadOnlySequence<byte>)
            if (method.Parameters.Length != 1) return null;
            tagName = method.Parameters[0].Type.ToDisplayString() switch
            {
                "System.ReadOnlySpan<byte>" => "DeserializeFromSpan",
                "System.Buffers.ReadOnlySequence<byte>" => "DeserializeFromSeq",
                _ => "",
            };
        }
        else
        {
            tagName = method.Name;
        }
        if (tagName.Length == 0) return null;

        var loc = ctx.SemanticModel.GetInterceptableLocation(inv, ct);
        if (loc is null) return null;

        return new CallSite(
            TargetFqn: target.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            InterceptVersion: loc.Version,
            InterceptData: loc.Data,
            MethodName: tagName);
    }

    // ----- Discovery: IProtoSerializerConfiguration<T> implementations -----
    private static ConfigInfo? TryExtractConfig(GeneratorSyntaxContext ctx, CancellationToken ct)
    {
        if (ctx.Node is not ClassDeclarationSyntax cls) return null;
        if (ctx.SemanticModel.GetDeclaredSymbol(cls, ct) is not INamedTypeSymbol cfgSym) return null;

        INamedTypeSymbol? iface = null;
        foreach (var i in cfgSym.AllInterfaces)
        {
            var od = i.OriginalDefinition;
            // Match by namespace + name + arity. Comparing OriginalDefinition.ToDisplayString()
            // against a bare FQN fails because the display string includes the "<T>" type parameter.
            if (i.IsGenericType && od.Arity == 1 &&
                od.Name == "IProtoSerializerConfiguration" &&
                od.ContainingNamespace?.ToDisplayString() == "Lite.Serialization.Protobuf.Fluent")
            {
                iface = i;
                break;
            }
        }
        if (iface is null) return null;
        if (iface.TypeArguments[0] is not INamedTypeSymbol target) return null;

        var configureMethod = cls.Members.OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(m => m.Identifier.Text == "Configure" && m.ParameterList.Parameters.Count == 1);
        if (configureMethod?.Body is null) return null;

        var paramName = configureMethod.ParameterList.Parameters[0].Identifier.Text;
        var overrides = ParseConfigureBody(configureMethod.Body, paramName);

        return new ConfigInfo(
            TargetFqn: target.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            Overrides: overrides.ToImmutableArray());
    }

    private static List<FieldOverride> ParseConfigureBody(BlockSyntax body, string builderParam)
    {
        var result = new List<FieldOverride>();
        foreach (var stmt in body.Statements)
        {
            if (stmt is not ExpressionStatementSyntax { Expression: InvocationExpressionSyntax tail }) continue;
            var ov = ParseFluentChain(tail, builderParam);
            if (ov is not null) result.Add(ov.Value);
        }
        return result;
    }

    private static FieldOverride? ParseFluentChain(InvocationExpressionSyntax tail, string builderParam)
    {
        string? propName = null;
        int? tag = null;
        string? name = null;
        bool ignore = false;

        InvocationExpressionSyntax? cur = tail;
        while (cur is not null)
        {
            if (cur.Expression is not MemberAccessExpressionSyntax ma) break;
            var methodName = ma.Name is GenericNameSyntax gn ? gn.Identifier.Text : ma.Name.Identifier.Text;

            switch (methodName)
            {
                case "Field":
                    if (ma.Expression is IdentifierNameSyntax idn && idn.Identifier.Text == builderParam &&
                        cur.ArgumentList.Arguments.Count == 1 &&
                        cur.ArgumentList.Arguments[0].Expression is SimpleLambdaExpressionSyntax sl &&
                        sl.ExpressionBody is MemberAccessExpressionSyntax spa)
                    {
                        propName = spa.Name.Identifier.Text;
                    }
                    cur = null;
                    continue;
                case "Tag":
                    if (cur.ArgumentList.Arguments.Count == 1 &&
                        cur.ArgumentList.Arguments[0].Expression is LiteralExpressionSyntax tl &&
                        int.TryParse(tl.Token.ValueText, out var t))
                        tag = t;
                    break;
                case "Name":
                    if (cur.ArgumentList.Arguments.Count == 1 &&
                        cur.ArgumentList.Arguments[0].Expression is LiteralExpressionSyntax nl)
                        name = nl.Token.ValueText;
                    break;
                case "Ignore":
                    ignore = true;
                    break;
            }

            cur = ma.Expression as InvocationExpressionSyntax;
        }

        if (propName is null) return null;
        return new FieldOverride(propName, tag, name, ignore);
    }

    // ----- Emission entry -----
    private static void Emit(SourceProductionContext spc, CSharpCompilation compilation, ImmutableArray<CallSite> sites,
        ImmutableArray<ConfigInfo> configs, ImmutableArray<GrpcMarshallerSite> grpcSites)
    {
        if (sites.IsDefaultOrEmpty && configs.IsDefaultOrEmpty && grpcSites.IsDefaultOrEmpty) return;

        var roots = new HashSet<string>(StringComparer.Ordinal);
        foreach (var s in sites) roots.Add(s.TargetFqn);
        foreach (var c in configs) roots.Add(c.TargetFqn);

        var configByFqn = configs.ToDictionary(c => c.TargetFqn, c => c, StringComparer.Ordinal);

        var typeModels = new Dictionary<string, TypeModel>(StringComparer.Ordinal);
        var enumModels = new Dictionary<string, EnumModel>(StringComparer.Ordinal);
        var queue = new Queue<INamedTypeSymbol>();
        foreach (var fqn in roots)
        {
            var sym = ResolveTypeByFqn(compilation, fqn);
            if (sym is not null) queue.Enqueue(sym);
        }

        while (queue.Count > 0)
        {
            var sym = queue.Dequeue();
            var fqn = sym.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            if (sym.TypeKind == TypeKind.Enum)
            {
                if (!enumModels.ContainsKey(fqn))
                    enumModels[fqn] = BuildEnumModel(sym);
                continue;
            }

            if (typeModels.ContainsKey(fqn)) continue;

            configByFqn.TryGetValue(fqn, out var cfg);
            var (model, refs, diags) = BuildTypeModel(sym, cfg);
            foreach (var d in diags) spc.ReportDiagnostic(d);
            if (model is null) continue;

            typeModels[fqn] = model;
            foreach (var refSym in refs)
                queue.Enqueue(refSym);
        }

        // gRPC.NET interop: for each Marshallers.Create<T>() site, try to build a model for T.
        // Only intercept types we can FULLY serialize (no error diagnostics) — partial is unsafe.
        // Wire format stays compatible because explicit FieldNumber consts give real proto tags.
        var grpcInterceptable = new List<GrpcMarshallerSite>();
        foreach (var gs in grpcSites)
        {
            if (typeModels.ContainsKey(gs.TargetFqn))
            {
                grpcInterceptable.Add(gs);
                continue;
            }
            var sym = ResolveTypeByFqn(compilation, gs.TargetFqn);
            if (sym is null || sym.TypeKind == TypeKind.Enum) continue;

            var (model, refs, diags) = BuildTypeModel(sym, null);
            if (model is null || diags.Any(d => d.Severity == DiagnosticSeverity.Error))
            {
                // unsupported shape (e.g. RepeatedField/MapField/oneof) — leave it to Google.Protobuf
                spc.ReportDiagnostic(Diagnostic.Create(GrpcInteropSkipped, Location.None, sym.Name));
                continue;
            }
            typeModels[gs.TargetFqn] = model;
            foreach (var refSym in refs)
            {
                var rfqn = refSym.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                if (refSym.TypeKind == TypeKind.Enum)
                {
                    if (!enumModels.ContainsKey(rfqn)) enumModels[rfqn] = BuildEnumModel(refSym);
                }
                else if (!typeModels.ContainsKey(rfqn))
                {
                    var (rm, rrefs, _) = BuildTypeModel(refSym, null);
                    if (rm is not null) typeModels[rfqn] = rm;
                }
            }
            grpcInterceptable.Add(gs);
        }

        if (typeModels.Count == 0 && enumModels.Count == 0) return;

        EmitInterceptsLocationAttribute(spc);
        foreach (var m in typeModels.Values) EmitSerializer(spc, m);
        if (!sites.IsDefaultOrEmpty) EmitInterceptors(spc, sites, typeModels);
        if (grpcInterceptable.Count > 0) EmitGrpcMarshallerInterceptors(spc, grpcInterceptable, typeModels);
        EmitProtoSchemas(spc, typeModels.Values, enumModels.Values);
    }

    private static void EmitGrpcMarshallerInterceptors(SourceProductionContext spc,
        List<GrpcMarshallerSite> grpcSites, IReadOnlyDictionary<string, TypeModel> typeModels)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("namespace Lite.Serialization.Protobuf.Generated;");
        sb.AppendLine();
        sb.AppendLine("file static class __GrpcMarshallerInterceptors");
        sb.AppendLine("{");
        var idx = 0;
        foreach (var gs in grpcSites)
        {
            if (!typeModels.TryGetValue(gs.TargetFqn, out var model)) continue;
            var serializerFqn = $"{(string.IsNullOrEmpty(model.Namespace) ? "" : model.Namespace + ".")}{model.TypeName}__ProtoSerializer";

            var paramList = string.Join(", ",
                gs.ParamTypes.Split('|').Where(p => p.Length > 0).Select((p, i) => $"{p} __a{i}"));

            sb.Append("    [global::System.Runtime.CompilerServices.InterceptsLocation(")
              .Append(gs.InterceptVersion).Append(", \"").Append(gs.InterceptData).AppendLine("\")]");
            sb.Append("    public static global::Grpc.Core.Marshaller<").Append(gs.TargetFqn).Append("> __GM_").Append(idx++)
              .Append('(').Append(paramList).AppendLine(")");
            sb.Append("        => global::").Append(serializerFqn).AppendLine(".Marshaller;");
        }
        sb.AppendLine("}");

        spc.AddSource("__GrpcMarshallerInterceptors.g.cs", sb.ToString());
    }

    private static INamedTypeSymbol? ResolveTypeByFqn(CSharpCompilation compilation, string fqn)
    {
        var clean = fqn.StartsWith("global::", StringComparison.Ordinal) ? fqn.Substring("global::".Length) : fqn;
        var sym = compilation.GetTypeByMetadataName(clean);
        if (sym is not null) return sym;
        foreach (var assembly in compilation.SourceModule.ReferencedAssemblySymbols.Prepend(compilation.Assembly))
        {
            var found = assembly.GetTypeByMetadataName(clean);
            if (found is not null) return found;
        }
        return null;
    }

    // ----- Build TypeModel -----
    private static (TypeModel? Model, IReadOnlyList<INamedTypeSymbol> Referenced, IReadOnlyList<Diagnostic> Diagnostics) BuildTypeModel(INamedTypeSymbol type, ConfigInfo? config)
    {
        var diags = new List<Diagnostic>();
        var referenced = new List<INamedTypeSymbol>();

        if (type.IsGenericType)
        {
            diags.Add(Diagnostic.Create(GenericNotSupported, type.Locations.FirstOrDefault(), type.Name));
            return (null, referenced, diags);
        }

        var configByPropName = (config?.Overrides ?? ImmutableArray<FieldOverride>.Empty).ToDictionary(o => o.PropertyName, StringComparer.Ordinal);

        var memberCandidates = new List<MemberInfo>();
        foreach (var m in type.GetMembers())
        {
            if (m.IsStatic) continue;
            if (m.DeclaredAccessibility != Accessibility.Public) continue;

            switch (m)
            {
                case IPropertySymbol prop:
                    if (prop.IsIndexer) continue;
                    if (prop.GetMethod is null || prop.GetMethod.DeclaredAccessibility != Accessibility.Public) continue;
                    memberCandidates.Add(new MemberInfo(
                        Name: prop.Name,
                        Type: prop.Type,
                        IsSettable: prop.SetMethod is not null && prop.SetMethod.DeclaredAccessibility == Accessibility.Public && !prop.SetMethod.IsInitOnly,
                        IsInitOnly: prop.SetMethod?.IsInitOnly == true,
                        IsField: false));
                    break;
                case IFieldSymbol fld when !fld.IsConst && !fld.IsImplicitlyDeclared:
                    memberCandidates.Add(new MemberInfo(
                        Name: fld.Name,
                        Type: fld.Type,
                        IsSettable: !fld.IsReadOnly,
                        IsInitOnly: false,
                        IsField: true));
                    break;
            }
        }

        var ignored = new HashSet<string>(StringComparer.Ordinal);
        foreach (var ov in configByPropName.Values)
            if (ov.Ignore) ignored.Add(ov.PropertyName);
        memberCandidates.RemoveAll(m => ignored.Contains(m.Name));

        if (memberCandidates.Count == 0)
        {
            diags.Add(Diagnostic.Create(NoMembers, type.Locations.FirstOrDefault(), type.Name));
            return (null, referenced, diags);
        }

        var fields = new List<FieldModel>();
        var seenTags = new Dictionary<int, string>();
        foreach (var m in memberCandidates)
        {
            var mapping = MapType(m.Type, referenced);
            if (mapping is null)
            {
                diags.Add(Diagnostic.Create(UnsupportedType, type.Locations.FirstOrDefault(), m.Type.ToDisplayString(), m.Name, type.Name));
                continue;
            }

            int tag;
            if (configByPropName.TryGetValue(m.Name, out var ov) && ov.Tag is not null)
                tag = ov.Tag.Value;                                  // 1) fluent config override
            else if (TryGetExplicitFieldNumber(type, m.Name, out var fieldNumber))
                tag = fieldNumber;                                   // 2) explicit `const int XxxFieldNumber` (Grpc.Tools-style)
            else
                tag = ComputeProtoTag(m.Name);                       // 3) deterministic name-hash

            if (seenTags.TryGetValue(tag, out var other))
            {
                diags.Add(Diagnostic.Create(DuplicateTag, type.Locations.FirstOrDefault(), tag, type.Name, $"{m.Name} and {other}"));
                continue;
            }
            seenTags[tag] = m.Name;

            string protoName;
            if (configByPropName.TryGetValue(m.Name, out var ov2) && !string.IsNullOrEmpty(ov2.Name))
                protoName = ov2.Name!;
            else
                protoName = ToSnakeCase(m.Name);

            fields.Add(new FieldModel(m.Name, protoName, tag, m, mapping.Value));
        }

        var construction = ChooseConstruction(type, fields, diags);
        if (construction is null) return (null, referenced, diags);

        return (new TypeModel(
            Namespace: type.ContainingNamespace?.IsGlobalNamespace == true ? "" : type.ContainingNamespace!.ToDisplayString(),
            TypeName: type.Name,
            TypeFqn: type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            ProtoMessageName: type.Name,
            ProtoPackage: type.ContainingNamespace?.IsGlobalNamespace == true ? "" : type.ContainingNamespace!.ToDisplayString().ToLowerInvariant(),
            IsValueType: type.IsValueType,
            IsRecord: type.IsRecord,
            Fields: fields,
            Construction: construction.Value), referenced, diags);
    }

    private static EnumModel BuildEnumModel(INamedTypeSymbol enumType)
    {
        var values = enumType.GetMembers().OfType<IFieldSymbol>()
            .Where(f => f.HasConstantValue && f.IsConst)
            .Select(f => new EnumValueModel(f.Name, System.Convert.ToInt32(f.ConstantValue)))
            .ToList();

        return new EnumModel(
            Namespace: enumType.ContainingNamespace?.IsGlobalNamespace == true ? "" : enumType.ContainingNamespace!.ToDisplayString(),
            TypeName: enumType.Name,
            TypeFqn: enumType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            ProtoEnumName: enumType.Name,
            ProtoPackage: enumType.ContainingNamespace?.IsGlobalNamespace == true ? "" : enumType.ContainingNamespace!.ToDisplayString().ToLowerInvariant(),
            Values: values);
    }

    private static ConstructionStrategy? ChooseConstruction(INamedTypeSymbol type, List<FieldModel> fields, List<Diagnostic> diags)
    {
        var fieldByName = fields.ToDictionary(f => f.MemberName, StringComparer.Ordinal);

        var ctors = type.InstanceConstructors
            .Where(c => c.DeclaredAccessibility == Accessibility.Public)
            .OrderByDescending(c => c.Parameters.Length)
            .ToList();

        IMethodSymbol? bestCtor = null;
        var bestCovered = -1;
        foreach (var c in ctors)
        {
            var allMatched = true;
            var matched = new List<string>();
            foreach (var p in c.Parameters)
            {
                var match = fields.FirstOrDefault(f => string.Equals(f.MemberName, p.Name, StringComparison.OrdinalIgnoreCase));
                if (match is null) { allMatched = false; break; }
                matched.Add(match.MemberName);
            }
            if (!allMatched) continue;
            if (matched.Count > bestCovered)
            {
                bestCovered = matched.Count;
                bestCtor = c;
            }
        }

        var allSettable = fields.All(f => f.Member.IsSettable);
        var hasParameterless = ctors.Any(c => c.Parameters.Length == 0);

        if (allSettable && hasParameterless)
            return new ConstructionStrategy(ConstructionMode.MutableSetter, Array.Empty<string>());

        if (bestCtor is not null)
        {
            var ctorParamMembers = bestCtor.Parameters.Select(p => fields.First(f => string.Equals(f.MemberName, p.Name, StringComparison.OrdinalIgnoreCase)).MemberName).ToArray();
            var notCovered = fields.Select(f => f.MemberName).Except(ctorParamMembers, StringComparer.Ordinal).ToList();
            foreach (var nm in notCovered)
            {
                if (!fieldByName[nm].Member.IsSettable)
                {
                    diags.Add(Diagnostic.Create(NotConstructible, type.Locations.FirstOrDefault(), nm, type.Name));
                    return null;
                }
            }
            return new ConstructionStrategy(
                notCovered.Count == 0 ? ConstructionMode.CtorOnly : ConstructionMode.CtorPlusSetters,
                ctorParamMembers);
        }

        if (type.IsValueType && allSettable)
            return new ConstructionStrategy(ConstructionMode.MutableSetter, Array.Empty<string>());

        diags.Add(Diagnostic.Create(NotConstructible, type.Locations.FirstOrDefault(), "<type>", type.Name));
        return null;
    }

    // ----- Type mapping -----
    private static WireMapping? MapType(ITypeSymbol type, List<INamedTypeSymbol> referenced)
    {
        // Map: Dictionary<K, V> / IDictionary<K, V> / IReadOnlyDictionary<K, V>
        if (TryDetectMap(type, out var keyType, out var valueType, out var dictConcrete))
        {
            var keyOpt = MapType(keyType, referenced);
            var valOpt = MapType(valueType, referenced);
            if (keyOpt is null || valOpt is null) return null;
            var k = keyOpt.Value;
            var v = valOpt.Value;
            if (!IsAllowedMapKey(k.Kind)) return null;
            if (k.IsRepeated || k.IsMap || v.IsRepeated || v.IsMap) return null;
            return new WireMapping(
                Kind: WireKind.Message, // unused for map
                ProtoType: $"map<{k.ProtoType}, {v.ProtoType}>",
                ClrTypeFqn: type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                IsMap: true,
                KeyKind: k.Kind,
                KeyClrFqn: k.ClrTypeFqn,
                KeyProtoType: k.ProtoType,
                ValueKind: v.Kind,
                ValueClrFqn: v.ClrTypeFqn,
                ValueProtoType: v.ProtoType,
                ValueNestedSerializerFqn: v.NestedSerializerFqn,
                ValueEnumUnderlyingFqn: v.EnumUnderlyingFqn,
                DictionaryConcrete: dictConcrete);
        }

        // Repeated: T[] / List<T> / IList<T> / IReadOnlyList<T>
        if (TryDetectCollection(type, out var elementType, out var collectionConcrete))
        {
            var innerOpt = MapType(elementType, referenced);
            if (innerOpt is null) return null;
            var inner = innerOpt.Value;
            if (inner.IsRepeated) return null; // repeated of repeated unsupported
            return new WireMapping(
                Kind: inner.Kind,
                ProtoType: inner.ProtoType,
                ClrTypeFqn: type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                IsOptional: false,
                InnerNonNullableFqn: null,
                IsRepeated: true,
                ElementClrTypeFqn: elementType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                CollectionConcrete: collectionConcrete,
                NestedSerializerFqn: inner.NestedSerializerFqn,
                EnumUnderlyingFqn: inner.EnumUnderlyingFqn);
        }

        // Nullable<T> for value types
        if (type is INamedTypeSymbol nullable && nullable.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
        {
            var innerOpt = MapType(nullable.TypeArguments[0], referenced);
            if (innerOpt is null) return null;
            var inner = innerOpt.Value;
            return inner with { IsOptional = true, ClrTypeFqn = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), InnerNonNullableFqn = nullable.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) };
        }

        // BCL specials
        var fqn = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        if (fqn == "global::System.Guid")
            return new WireMapping(WireKind.Guid, "bytes", fqn);
        if (fqn == "global::System.DateTime")
            return new WireMapping(WireKind.DateTime, "int64", fqn);
        if (fqn == "global::System.Decimal" || fqn == "decimal")
            return new WireMapping(WireKind.Decimal, "bytes", fqn);

        // byte[]
        if (type is IArrayTypeSymbol arr && arr.ElementType.SpecialType == SpecialType.System_Byte)
            return new WireMapping(WireKind.Bytes, "bytes", fqn);

        // Enum
        if (type.TypeKind == TypeKind.Enum && type is INamedTypeSymbol enumSym)
        {
            referenced.Add(enumSym);
            var underlying = enumSym.EnumUnderlyingType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? "global::System.Int32";
            return new WireMapping(
                Kind: WireKind.Enum,
                ProtoType: enumSym.Name,
                ClrTypeFqn: fqn,
                EnumUnderlyingFqn: underlying);
        }

        // Primitives
        switch (type.SpecialType)
        {
            case SpecialType.System_Int32: return new WireMapping(WireKind.Int32, "int32", fqn);
            case SpecialType.System_Int64: return new WireMapping(WireKind.Int64, "int64", fqn);
            case SpecialType.System_UInt32: return new WireMapping(WireKind.UInt32, "uint32", fqn);
            case SpecialType.System_UInt64: return new WireMapping(WireKind.UInt64, "uint64", fqn);
            case SpecialType.System_Boolean: return new WireMapping(WireKind.Bool, "bool", fqn);
            case SpecialType.System_Single: return new WireMapping(WireKind.Float, "float", fqn);
            case SpecialType.System_Double: return new WireMapping(WireKind.Double, "double", fqn);
            case SpecialType.System_String: return new WireMapping(WireKind.String, "string", fqn);
            case SpecialType.System_Byte: return new WireMapping(WireKind.UInt32WidenFromByte, "uint32", fqn);
            case SpecialType.System_SByte: return new WireMapping(WireKind.Int32WidenFromSByte, "int32", fqn);
            case SpecialType.System_Int16: return new WireMapping(WireKind.Int32WidenFromShort, "int32", fqn);
            case SpecialType.System_UInt16: return new WireMapping(WireKind.UInt32WidenFromUShort, "uint32", fqn);
        }

        // Nested POCO (class/struct/record)
        if (type is INamedTypeSymbol nested && (nested.TypeKind == TypeKind.Class || nested.TypeKind == TypeKind.Struct) && !nested.IsAbstract)
        {
            referenced.Add(nested);
            var ns = nested.ContainingNamespace?.IsGlobalNamespace == true ? "" : nested.ContainingNamespace!.ToDisplayString();
            var serializerFqn = $"global::{(string.IsNullOrEmpty(ns) ? "" : ns + ".")}{nested.Name}__ProtoSerializer";
            return new WireMapping(
                Kind: WireKind.Message,
                ProtoType: nested.Name,
                ClrTypeFqn: fqn,
                NestedSerializerFqn: serializerFqn);
        }

        return null;
    }

    private static bool TryDetectMap(ITypeSymbol t, out ITypeSymbol keyType, out ITypeSymbol valueType, out string concrete)
    {
        if (t is INamedTypeSymbol named && named.IsGenericType && named.TypeArguments.Length == 2)
        {
            var def = named.OriginalDefinition.ToDisplayString();
            if (def == "System.Collections.Generic.Dictionary<TKey, TValue>")
            {
                keyType = named.TypeArguments[0]; valueType = named.TypeArguments[1]; concrete = "Dictionary"; return true;
            }
            if (def == "System.Collections.Generic.IDictionary<TKey, TValue>")
            {
                keyType = named.TypeArguments[0]; valueType = named.TypeArguments[1]; concrete = "IDictionary"; return true;
            }
            if (def == "System.Collections.Generic.IReadOnlyDictionary<TKey, TValue>")
            {
                keyType = named.TypeArguments[0]; valueType = named.TypeArguments[1]; concrete = "IReadOnlyDictionary"; return true;
            }
        }
        keyType = null!; valueType = null!; concrete = "";
        return false;
    }

    private static bool IsAllowedMapKey(WireKind k) => k switch
    {
        WireKind.Int32 or WireKind.Int64 or WireKind.UInt32 or WireKind.UInt64
            or WireKind.Bool or WireKind.String
            or WireKind.Int32WidenFromSByte or WireKind.Int32WidenFromShort
            or WireKind.UInt32WidenFromByte or WireKind.UInt32WidenFromUShort => true,
        _ => false,
    };

    private static bool TryDetectCollection(ITypeSymbol t, out ITypeSymbol element, out string concrete)
    {
        if (t is IArrayTypeSymbol arr && arr.ElementType.SpecialType != SpecialType.System_Byte)
        {
            element = arr.ElementType;
            concrete = "Array";
            return true;
        }
        if (t is INamedTypeSymbol named && named.IsGenericType)
        {
            var def = named.OriginalDefinition.ToDisplayString();
            if (def == "System.Collections.Generic.List<T>")
            {
                element = named.TypeArguments[0];
                concrete = "List";
                return true;
            }
            if (def == "System.Collections.Generic.IList<T>")
            {
                element = named.TypeArguments[0];
                concrete = "IList";
                return true;
            }
            if (def == "System.Collections.Generic.IReadOnlyList<T>")
            {
                element = named.TypeArguments[0];
                concrete = "IReadOnlyList";
                return true;
            }
        }
        element = null!;
        concrete = "";
        return false;
    }

    // Looks for an explicit `public const int <PropName>FieldNumber = N;` on the type
    // (the convention emitted by Grpc.Tools / Google.Protobuf-generated messages).
    private static bool TryGetExplicitFieldNumber(INamedTypeSymbol type, string propName, out int tag)
    {
        var constName = propName + "FieldNumber";
        var cur = type;
        while (cur is not null)
        {
            foreach (var member in cur.GetMembers(constName))
            {
                if (member is IFieldSymbol { IsConst: true, ConstantValue: int n })
                {
                    tag = n;
                    return true;
                }
            }
            cur = cur.BaseType;
        }
        tag = 0;
        return false;
    }

    // ----- Tag computation -----
    private static int ComputeProtoTag(string memberName)
    {
        const uint offsetBasis = 2166136261u;
        const uint prime = 16777619u;
        const int validMin = 1;
        const int validMax = 536_870_911;
        const int reservedMin = 19_000;
        const int reservedMax = 19_999;

        uint h = offsetBasis;
        foreach (var c in memberName)
        {
            h ^= c;
            h = (uint)(h * prime);
        }
        var spanSize = (uint)(validMax - validMin + 1) - (uint)(reservedMax - reservedMin + 1);
        var mapped = (int)(h % spanSize) + validMin;
        if (mapped >= reservedMin) mapped += (reservedMax - reservedMin + 1);
        return mapped;
    }

    // ----- Emit serializer -----
    private static void EmitSerializer(SourceProductionContext spc, TypeModel m)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Buffers;");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine();

        var hasNs = !string.IsNullOrEmpty(m.Namespace);
        if (hasNs)
        {
            sb.Append("namespace ").Append(m.Namespace).AppendLine(";");
            sb.AppendLine();
        }

        var className = $"{m.TypeName}__ProtoSerializer";

        sb.Append("internal sealed class ").Append(className)
          .Append(" : global::Lite.Serialization.Protobuf.IProtoSerializer<").Append(m.TypeFqn).AppendLine(">");
        sb.AppendLine("{");
        sb.Append("    public static readonly ").Append(className).AppendLine(" Instance = new();");
        sb.Append("    public static readonly global::Grpc.Core.Marshaller<").Append(m.TypeFqn)
          .Append("> Marshaller = global::Lite.Serialization.Protobuf.LiteSerializer.CreateMarshaller<").Append(m.TypeFqn).AppendLine(">(Instance);");
        sb.AppendLine();

        // STATIC WriteTo orchestrator — ComputeSize + GetSpan + WriteToSpan + Advance
        sb.Append("    public static void WriteTo(in ").Append(m.TypeFqn).AppendLine(" value, global::System.Buffers.IBufferWriter<byte> writer)");
        sb.AppendLine("    {");
        sb.AppendLine("        var __sz = ComputeSize(value);");
        sb.AppendLine("        var __dst = writer.GetSpan(__sz);");
        sb.AppendLine("        var __wrote = WriteToSpan(in value, __dst);");
        sb.AppendLine("        writer.Advance(__wrote);");
        sb.AppendLine("    }");
        sb.AppendLine();

        // STATIC WriteToSpan — writes to caller's Span<byte>, returns actual bytes written. Zero-alloc fast path.
        sb.Append("    public static int WriteToSpan(in ").Append(m.TypeFqn).AppendLine(" value, global::System.Span<byte> destination)");
        sb.AppendLine("    {");
        sb.AppendLine("        var w = new global::Lite.Serialization.Protobuf.WireFormat.SpanProtoWriter(destination);");
        foreach (var f in m.Fields) EmitWriteField(sb, f);
        sb.AppendLine("        return w.Written;");
        sb.AppendLine("    }");
        sb.AppendLine();

        // STATIC ComputeSize — exact serialized size in bytes
        sb.Append("    public static int ComputeSize(").Append(m.TypeFqn).AppendLine(" value)");
        sb.AppendLine("    {");
        sb.AppendLine("        int size = 0;");
        foreach (var f in m.Fields) EmitSizeField(sb, f);
        sb.AppendLine("        return size;");
        sb.AppendLine("    }");
        sb.AppendLine();

        // STATIC ReadFrom orchestrator — dispatches to ReadFromSpan
        sb.Append("    public static ").Append(m.TypeFqn).AppendLine(" ReadFrom(global::System.Buffers.ReadOnlySequence<byte> source)");
        sb.AppendLine("    {");
        sb.AppendLine("        if (source.IsSingleSegment) return ReadFromSpan(source.FirstSpan);");
        sb.AppendLine("        var __len = (int)source.Length;");
        sb.AppendLine("        var __pool = global::System.Buffers.ArrayPool<byte>.Shared;");
        sb.AppendLine("        var __rented = __pool.Rent(__len);");
        sb.AppendLine("        try { source.CopyTo(__rented); return ReadFromSpan(__rented.AsSpan(0, __len)); }");
        sb.AppendLine("        finally { __pool.Return(__rented); }");
        sb.AppendLine("    }");
        sb.AppendLine();

        // STATIC ReadFromSpan — fast path. No virtual calls; struct reader directly over span.
        sb.Append("    public static ").Append(m.TypeFqn).AppendLine(" ReadFromSpan(global::System.ReadOnlySpan<byte> source)");
        sb.AppendLine("    {");
        sb.AppendLine("        var r = new global::Lite.Serialization.Protobuf.WireFormat.SpanProtoReader(source);");
        foreach (var f in m.Fields) EmitDeclareLocal(sb, f);
        sb.AppendLine("        while (r.TryReadTag(out var fieldNumber, out var wireType))");
        sb.AppendLine("        {");
        sb.AppendLine("            switch (fieldNumber)");
        sb.AppendLine("            {");
        foreach (var f in m.Fields) EmitReadCase(sb, f);
        sb.AppendLine("                default: r.SkipField(wireType); break;");
        sb.AppendLine("            }");
        sb.AppendLine("        }");
        EmitConstruct(sb, m);
        sb.AppendLine("    }");
        sb.AppendLine();

        // Explicit interface impl — delegates to static fast-path
        sb.Append("    void global::Lite.Serialization.Protobuf.IProtoSerializer<").Append(m.TypeFqn)
          .Append(">.WriteTo(in ").Append(m.TypeFqn).AppendLine(" value, global::System.Buffers.IBufferWriter<byte> writer)");
        sb.AppendLine("        => WriteTo(value, writer);");
        sb.AppendLine();
        sb.Append("    ").Append(m.TypeFqn).Append(" global::Lite.Serialization.Protobuf.IProtoSerializer<").Append(m.TypeFqn)
          .AppendLine(">.ReadFrom(global::System.Buffers.ReadOnlySequence<byte> source)");
        sb.AppendLine("        => ReadFrom(source);");
        sb.AppendLine("}");

        var hint = (hasNs ? m.Namespace + "." : "") + m.TypeName + ".LiteProtoSerializer.g.cs";
        spc.AddSource(hint, sb.ToString());
    }

    // Wire type values (local mirror of runtime ProtoWireType)
    private const int WT_Varint = 0;
    private const int WT_Fixed64 = 1;
    private const int WT_LengthDelimited = 2;
    private const int WT_Fixed32 = 5;

    // Compute exact varint encoding length of a (compile-time-known) tag value.
    private static int TagVarintSize(int fieldNumber, int wireType)
    {
        var tag = ((uint)fieldNumber << 3) | (uint)wireType;
        var n = 1;
        while (tag >= 0x80) { tag >>= 7; n++; }
        return n;
    }

    private static int WireTypeFor(WireKind k) => k switch
    {
        WireKind.Float => WT_Fixed32,
        WireKind.Double => WT_Fixed64,
        WireKind.String or WireKind.Bytes or WireKind.Message or WireKind.Guid or WireKind.Decimal => WT_LengthDelimited,
        _ => WT_Varint, // int*, uint*, bool, enum, datetime (as int64 ticks), widened
    };

    private const string VarintSizeFqn = "global::Lite.Serialization.Protobuf.WireFormat.ProtoWriter.VarintSize";

    // C# expression evaluating to byte count of the SCALAR encoding (no tag, no length prefix for length-delim).
    private static string ScalarValueSizeExpr(WireKind k, string vexpr) => k switch
    {
        WireKind.Bool => "1",
        WireKind.Float => "4",
        WireKind.Double => "8",
        WireKind.Int32 => $"{VarintSizeFqn}((ulong)(long)(int){vexpr})",
        WireKind.Int64 => $"{VarintSizeFqn}((ulong){vexpr})",
        WireKind.UInt32 => $"{VarintSizeFqn}((ulong)(uint){vexpr})",
        WireKind.UInt64 => $"{VarintSizeFqn}({vexpr})",
        WireKind.Int32WidenFromSByte or WireKind.Int32WidenFromShort => $"{VarintSizeFqn}((ulong)(long)(int){vexpr})",
        WireKind.UInt32WidenFromByte or WireKind.UInt32WidenFromUShort => $"{VarintSizeFqn}((ulong)(uint){vexpr})",
        WireKind.Enum => $"{VarintSizeFqn}((ulong)(long)(int){vexpr})",
        WireKind.DateTime => $"{VarintSizeFqn}((ulong){vexpr}.ToUniversalTime().Ticks)",
        _ => "0",
    };

    private static void EmitSizeField(StringBuilder sb, FieldModel f)
    {
        var member = $"value.{f.MemberName}";
        var k = f.Mapping.Kind;
        var tagSz = TagVarintSize(f.Tag, WireTypeFor(k));

        if (f.Mapping.IsMap)
        {
            // Each entry: tag + lenVarint(entrySize) + entrySize
            // entrySize = keyTag(=1, 1 byte) + scalar_size(key) + valueTag(=2, 1 byte) + value_size
            var mapTagSz = TagVarintSize(f.Tag, WT_LengthDelimited);
            sb.Append("        if (").Append(member).AppendLine(" is not null)");
            sb.AppendLine("        {");
            sb.Append("            foreach (var __kv in ").Append(member).AppendLine(")");
            sb.AppendLine("            {");
            EmitMapKeySize(sb, f.Mapping, "                ");
            EmitMapValueSize(sb, f.Mapping, "                ");
            sb.AppendLine("                int __entry = 1 + __keySz + 1 + __valSz;");
            sb.Append("                size += ").Append(mapTagSz).Append(" + ").Append(VarintSizeFqn).AppendLine("((ulong)__entry) + __entry;");
            sb.AppendLine("            }");
            sb.AppendLine("        }");
            return;
        }

        if (f.Mapping.IsRepeated)
        {
            var elem = f.Mapping.ElementClrTypeFqn!;
            var lenAccess = f.Mapping.CollectionConcrete == "Array" ? "Length" : "Count";
            var elemTagSz = TagVarintSize(f.Tag, WireTypeFor(k));

            sb.Append("        if (").Append(member).Append(" is not null && ").Append(member).Append(".").Append(lenAccess).AppendLine(" > 0)");
            sb.AppendLine("        {");
            if (k == WireKind.String)
            {
                sb.Append("            foreach (var __v in ").Append(member).AppendLine(")");
                sb.AppendLine("            {");
                sb.Append("                if (__v is null) { size += ").Append(elemTagSz).AppendLine(" + 1; }");
                sb.AppendLine("                else {");
                sb.AppendLine("                    int __bc = global::System.Text.Encoding.UTF8.GetByteCount(__v);");
                sb.Append("                    size += ").Append(elemTagSz).Append(" + ").Append(VarintSizeFqn).AppendLine("((ulong)__bc) + __bc;");
                sb.AppendLine("                }");
                sb.AppendLine("            }");
            }
            else if (k == WireKind.Bytes)
            {
                sb.Append("            foreach (var __v in ").Append(member).AppendLine(")");
                sb.AppendLine("            {");
                sb.Append("                int __bc = __v is null ? 0 : __v.Length;");
                sb.Append("                size += ").Append(elemTagSz).Append(" + ").Append(VarintSizeFqn).AppendLine("((ulong)__bc) + __bc;");
                sb.AppendLine("            }");
            }
            else if (k == WireKind.Message)
            {
                sb.Append("            foreach (var __v in ").Append(member).AppendLine(")");
                sb.AppendLine("            {");
                sb.Append("                int __ns = __v is null ? 0 : ").Append(f.Mapping.NestedSerializerFqn).AppendLine(".ComputeSize(__v);");
                sb.Append("                size += ").Append(elemTagSz).Append(" + ").Append(VarintSizeFqn).AppendLine("((ulong)__ns) + __ns;");
                sb.AppendLine("            }");
            }
            else
            {
                // packed scalars: tag + payloadLenVarint + payloadBytes
                var packedTagSz = TagVarintSize(f.Tag, WT_LengthDelimited);
                sb.AppendLine("            int __payload = 0;");
                sb.Append("            foreach (var __v in ").Append(member).Append(") __payload += ").Append(ScalarValueSizeExpr(k, "__v")).AppendLine(";");
                sb.Append("            size += ").Append(packedTagSz).Append(" + ").Append(VarintSizeFqn).AppendLine("((ulong)__payload) + __payload;");
            }
            sb.AppendLine("        }");
            return;
        }

        if (f.Mapping.IsOptional)
        {
            sb.Append("        if (").Append(member).AppendLine(".HasValue)");
            sb.Append("            size += ").Append(tagSz).Append(" + ").Append(ScalarValueSizeExpr(k, $"{member}.Value")).AppendLine(";");
            return;
        }

        switch (k)
        {
            case WireKind.String:
                sb.Append("        if (").Append(member).AppendLine(" is { Length: > 0 })");
                sb.AppendLine("        {");
                sb.Append("            int __bc = global::System.Text.Encoding.UTF8.GetByteCount(").Append(member).AppendLine(");");
                sb.Append("            size += ").Append(tagSz).Append(" + ").Append(VarintSizeFqn).AppendLine("((ulong)__bc) + __bc;");
                sb.AppendLine("        }");
                return;
            case WireKind.Bytes:
                sb.Append("        if (").Append(member).AppendLine(" is { Length: > 0 })");
                sb.Append("            size += ").Append(tagSz).Append(" + ").Append(VarintSizeFqn).Append("((ulong)").Append(member).Append(".Length) + ").Append(member).AppendLine(".Length;");
                return;
            case WireKind.Message:
                sb.Append("        if (").Append(member).AppendLine(" is not null)");
                sb.AppendLine("        {");
                sb.Append("            int __ns = ").Append(f.Mapping.NestedSerializerFqn).Append(".ComputeSize(").Append(member).AppendLine(");");
                sb.Append("            size += ").Append(tagSz).Append(" + ").Append(VarintSizeFqn).AppendLine("((ulong)__ns) + __ns;");
                sb.AppendLine("        }");
                return;
            case WireKind.Guid:
                // 16 byte body, 1 byte length varint (16 < 128)
                sb.Append("        if (").Append(member).Append(" != global::System.Guid.Empty) size += ").Append(tagSz + 1 + 16).AppendLine(";");
                return;
            case WireKind.Decimal:
                sb.Append("        if (").Append(member).Append(" != 0m) size += ").Append(tagSz + 1 + 16).AppendLine(";");
                return;
            case WireKind.DateTime:
                sb.Append("        if (").Append(member).AppendLine(" != default(global::System.DateTime))");
                sb.Append("            size += ").Append(tagSz).Append(" + ").Append(ScalarValueSizeExpr(WireKind.DateTime, member)).AppendLine(";");
                return;
            case WireKind.Enum:
                sb.Append("        if (").Append(member).Append(" != default(").Append(f.Mapping.ClrTypeFqn).AppendLine("))");
                sb.Append("            size += ").Append(tagSz).Append(" + ").Append(ScalarValueSizeExpr(WireKind.Enum, member)).AppendLine(";");
                return;
        }

        // primitives — Varint or Fixed
        sb.Append("        if (").Append(DefaultSkipCondition(k, member)).AppendLine(")");
        sb.Append("            size += ").Append(tagSz).Append(" + ").Append(ScalarValueSizeExpr(k, member)).AppendLine(";");
    }

    private static void EmitMapKeySize(StringBuilder sb, WireMapping m, string indent)
    {
        // Sets a local `int __keySz` based on m.KeyKind and __kv.Key.
        // Proto map keys are integral, bool, or string (never bytes/float/double/message),
        // so a length-delimited key means string; everything else is a scalar varint.
        if (m.KeyKind == WireKind.String)
        {
            sb.Append(indent).AppendLine("int __keySz;");
            sb.Append(indent).AppendLine("if (__kv.Key is null) __keySz = 1;");
            sb.Append(indent).Append("else { int __kbc = global::System.Text.Encoding.UTF8.GetByteCount(__kv.Key); __keySz = ").Append(VarintSizeFqn).AppendLine("((ulong)__kbc) + __kbc; }");
        }
        else
        {
            sb.Append(indent).Append("int __keySz = ").Append(ScalarValueSizeExpr(m.KeyKind, "__kv.Key")).AppendLine(";");
        }
    }

    private static void EmitMapValueSize(StringBuilder sb, WireMapping m, string indent)
    {
        // Sets a local `int __valSz` based on m.ValueKind and __kv.Value
        switch (m.ValueKind)
        {
            case WireKind.String:
                sb.Append(indent).AppendLine("int __valSz;");
                sb.Append(indent).AppendLine("if (__kv.Value is null) __valSz = 1;");
                sb.Append(indent).Append("else { int __bc = global::System.Text.Encoding.UTF8.GetByteCount(__kv.Value); __valSz = ").Append(VarintSizeFqn).AppendLine("((ulong)__bc) + __bc; }");
                return;
            case WireKind.Bytes:
                sb.Append(indent).AppendLine("int __valSz;");
                sb.Append(indent).AppendLine("if (__kv.Value is null) __valSz = 1;");
                sb.Append(indent).Append("else { int __bc = __kv.Value.Length; __valSz = ").Append(VarintSizeFqn).AppendLine("((ulong)__bc) + __bc; }");
                return;
            case WireKind.Message:
                sb.Append(indent).AppendLine("int __valSz;");
                sb.Append(indent).AppendLine("if (__kv.Value is null) __valSz = 1;");
                sb.Append(indent).Append("else { int __ns = ").Append(m.ValueNestedSerializerFqn).Append(".ComputeSize(__kv.Value); __valSz = ").Append(VarintSizeFqn).AppendLine("((ulong)__ns) + __ns; }");
                return;
            case WireKind.Guid:
            case WireKind.Decimal:
                sb.Append(indent).AppendLine("int __valSz = 1 + 16;"); // varint(16) length prefix (1) + 16 payload bytes
                return;
            default:
                sb.Append(indent).Append("int __valSz = ").Append(ScalarValueSizeExpr(m.ValueKind, "__kv.Value")).AppendLine(";");
                return;
        }
    }

    private static void EmitWriteField(StringBuilder sb, FieldModel f)
    {
        var member = $"value.{f.MemberName}";

        if (f.Mapping.IsMap)
        {
            EmitWriteMap(sb, f, member);
            return;
        }

        if (f.Mapping.IsRepeated)
        {
            EmitWriteRepeated(sb, f, member);
            return;
        }

        if (f.Mapping.IsOptional)
        {
            sb.Append("        if (").Append(member).AppendLine(".HasValue)");
            sb.AppendLine("        {");
            sb.Append("            ");
            EmitWriteScalar(sb, f.Mapping.Kind, f.Tag, $"{member}.Value", f.Mapping);
            sb.AppendLine();
            sb.AppendLine("        }");
            return;
        }

        switch (f.Mapping.Kind)
        {
            case WireKind.String:
                sb.Append("        if (!string.IsNullOrEmpty(").Append(member).Append(")) w.WriteString(").Append(f.Tag).Append(", ").Append(member).AppendLine(");");
                return;
            case WireKind.Bytes:
                sb.Append("        if (").Append(member).Append(" is { Length: > 0 }) w.WriteBytes(").Append(f.Tag).Append(", ").Append(member).AppendLine(");");
                return;
            case WireKind.Message:
                AppendDirectMessageWrite(sb, "        ", "w", f.Tag, member, f.Mapping.NestedSerializerFqn!);
                return;
            case WireKind.Guid:
                sb.Append("        if (").Append(member).AppendLine(" != global::System.Guid.Empty)");
                sb.AppendLine("        {");
                sb.AppendLine("            global::System.Span<byte> __guidBuf = stackalloc byte[16];");
                sb.Append("            ").Append(member).AppendLine(".TryWriteBytes(__guidBuf);");
                sb.Append("            w.WriteFixedLengthBytes(").Append(f.Tag).AppendLine(", __guidBuf);");
                sb.AppendLine("        }");
                return;
            case WireKind.DateTime:
                sb.Append("        if (").Append(member).AppendLine(" != default(global::System.DateTime))");
                sb.Append("            w.WriteInt64(").Append(f.Tag).Append(", ").Append(member).AppendLine(".ToUniversalTime().Ticks);");
                return;
            case WireKind.Decimal:
                sb.Append("        if (").Append(member).AppendLine(" != 0m)");
                sb.AppendLine("        {");
                sb.Append("            global::System.Span<int> __decBits = stackalloc int[4]; global::System.Decimal.GetBits(").Append(member).AppendLine(", __decBits);");
                sb.AppendLine("            global::System.Span<byte> __decBuf = stackalloc byte[16];");
                sb.AppendLine("            global::System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(__decBuf.Slice(0, 4), __decBits[0]);");
                sb.AppendLine("            global::System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(__decBuf.Slice(4, 4), __decBits[1]);");
                sb.AppendLine("            global::System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(__decBuf.Slice(8, 4), __decBits[2]);");
                sb.AppendLine("            global::System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(__decBuf.Slice(12, 4), __decBits[3]);");
                sb.Append("            w.WriteFixedLengthBytes(").Append(f.Tag).AppendLine(", __decBuf);");
                sb.AppendLine("        }");
                return;
            case WireKind.Enum:
                sb.Append("        if (").Append(member).Append(" != default(").Append(f.Mapping.ClrTypeFqn).AppendLine("))");
                sb.Append("            w.WriteInt32(").Append(f.Tag).Append(", (int)").Append(member).AppendLine(");");
                return;
        }

        // primitives with default-skip
        sb.Append("        if (").Append(DefaultSkipCondition(f.Mapping.Kind, member)).Append(") ");
        EmitWriteScalar(sb, f.Mapping.Kind, f.Tag, member, f.Mapping);
        sb.AppendLine();
    }

    // Writes a nested message DIRECTLY into the span: tag + length(ComputeSize) + the message's own
    // WriteToSpan into the free region. No temp PooledBufferWriter, no copy, no interface dispatch —
    // this is what keeps nested/repeated message serialization allocation-free. Self-contained block,
    // so the local `__dm` never collides across call sites.
    private static void AppendDirectMessageWrite(StringBuilder sb, string indent, string writerVar, int tag, string valueExpr, string serFqn)
    {
        sb.Append(indent).Append("{ var __dm = ").Append(valueExpr).Append(";").AppendLine();
        sb.Append(indent).Append("  if (__dm is not null) {").AppendLine();
        sb.Append(indent).Append("    ").Append(writerVar).Append(".WriteRawTag(").Append(tag).AppendLine(", global::Lite.Serialization.Protobuf.WireFormat.ProtoWireType.LengthDelimited);");
        sb.Append(indent).Append("    int __dms = ").Append(serFqn).AppendLine(".ComputeSize(__dm);");
        sb.Append(indent).Append("    ").Append(writerVar).AppendLine(".WriteRawVarint((ulong)__dms);");
        sb.Append(indent).Append("    ").Append(writerVar).Append(".Advance(").Append(serFqn).Append(".WriteToSpan(in __dm, ").Append(writerVar).AppendLine(".FreeSpan));");
        sb.Append(indent).AppendLine("} }");
    }

    private static void EmitWriteRepeated(StringBuilder sb, FieldModel f, string member)
    {
        var k = f.Mapping.Kind;
        var lenAccess = f.Mapping.CollectionConcrete == "Array" ? "Length" : "Count";

        sb.AppendLine("        {");
        sb.Append("            var __r = ").Append(member).AppendLine(";");
        sb.Append("            if (__r is not null && __r.").Append(lenAccess).AppendLine(" > 0)");
        sb.AppendLine("            {");

        if (IsPackable(k))
        {
            sb.AppendLine("                int __payload = 0;");
            sb.AppendLine("                foreach (var __v in __r) __payload += " + PackedItemSizeExpr(k, "__v") + ";");
            sb.Append("                w.WritePackedVarintHeader(").Append(f.Tag).AppendLine(", __payload);");
            sb.AppendLine("                foreach (var __v in __r) " + PackedWriteItemExpr(k, "__v") + ";");
        }
        else
        {
            sb.AppendLine("                foreach (var __v in __r)");
            sb.AppendLine("                {");
            sb.Append("                    ");
            EmitWriteRepeatedItem(sb, k, f.Tag, "__v", f.Mapping);
            sb.AppendLine();
            sb.AppendLine("                }");
        }

        sb.AppendLine("            }");
        sb.AppendLine("        }");
    }

    private static void EmitWriteMap(StringBuilder sb, FieldModel f, string member)
    {
        sb.AppendLine("        {");
        sb.Append("            var __m = ").Append(member).AppendLine(";");
        sb.AppendLine("            if (__m is not null && __m.Count > 0)");
        sb.AppendLine("            {");
        sb.AppendLine("                foreach (var __kv in __m)");
        sb.AppendLine("                {");
        // Entry size — identical formula to ComputeSize, so the length prefix is exact and we can
        // write key+value straight into the span (no temp PooledBufferWriter per entry).
        EmitMapKeySize(sb, f.Mapping, "                    ");
        EmitMapValueSize(sb, f.Mapping, "                    ");
        sb.AppendLine("                    int __entry = 1 + __keySz + 1 + __valSz;");
        sb.Append("                    w.WriteRawTag(").Append(f.Tag).AppendLine(", global::Lite.Serialization.Protobuf.WireFormat.ProtoWireType.LengthDelimited);");
        sb.AppendLine("                    w.WriteRawVarint((ulong)__entry);");
        // Key (tag 1)
        sb.Append("                    ");
        EmitMapKvWrite(sb, "w", 1, "__kv.Key", f.Mapping.KeyKind, null, null);
        sb.AppendLine();
        // Value (tag 2)
        sb.Append("                    ");
        EmitMapKvWrite(sb, "w", 2, "__kv.Value", f.Mapping.ValueKind, f.Mapping.ValueClrFqn, f.Mapping.ValueNestedSerializerFqn);
        sb.AppendLine();
        sb.AppendLine("                }");
        sb.AppendLine("            }");
        sb.AppendLine("        }");
    }

    private static void EmitMapKvWrite(StringBuilder sb, string writerVar, int tag, string valueExpr, WireKind kind, string? valueFqn, string? nestedSerializerFqn)
    {
        switch (kind)
        {
            case WireKind.Int32: sb.Append(writerVar).Append(".WriteInt32(").Append(tag).Append(", ").Append(valueExpr).Append(");"); return;
            case WireKind.Int64: sb.Append(writerVar).Append(".WriteInt64(").Append(tag).Append(", ").Append(valueExpr).Append(");"); return;
            case WireKind.UInt32: sb.Append(writerVar).Append(".WriteUInt32(").Append(tag).Append(", ").Append(valueExpr).Append(");"); return;
            case WireKind.UInt64: sb.Append(writerVar).Append(".WriteUInt64(").Append(tag).Append(", ").Append(valueExpr).Append(");"); return;
            case WireKind.Bool: sb.Append(writerVar).Append(".WriteBool(").Append(tag).Append(", ").Append(valueExpr).Append(");"); return;
            case WireKind.Float: sb.Append(writerVar).Append(".WriteFloat(").Append(tag).Append(", ").Append(valueExpr).Append(");"); return;
            case WireKind.Double: sb.Append(writerVar).Append(".WriteDouble(").Append(tag).Append(", ").Append(valueExpr).Append(");"); return;
            case WireKind.String: sb.Append(writerVar).Append(".WriteString(").Append(tag).Append(", ").Append(valueExpr).Append(" ?? \"\");"); return;
            case WireKind.Bytes: sb.Append(writerVar).Append(".WriteBytes(").Append(tag).Append(", ").Append(valueExpr).Append(");"); return;
            case WireKind.Enum: sb.Append(writerVar).Append(".WriteInt32(").Append(tag).Append(", (int)").Append(valueExpr).Append(");"); return;
            case WireKind.Int32WidenFromSByte:
            case WireKind.Int32WidenFromShort: sb.Append(writerVar).Append(".WriteInt32(").Append(tag).Append(", (int)").Append(valueExpr).Append(");"); return;
            case WireKind.UInt32WidenFromByte:
            case WireKind.UInt32WidenFromUShort: sb.Append(writerVar).Append(".WriteUInt32(").Append(tag).Append(", (uint)").Append(valueExpr).Append(");"); return;
            case WireKind.DateTime: sb.Append(writerVar).Append(".WriteInt64(").Append(tag).Append(", ").Append(valueExpr).Append(".ToUniversalTime().Ticks);"); return;
            case WireKind.Guid:
                sb.AppendLine("{");
                sb.AppendLine("                            global::System.Span<byte> __gb = stackalloc byte[16];");
                sb.Append("                            ").Append(valueExpr).AppendLine(".TryWriteBytes(__gb);");
                sb.Append("                            ").Append(writerVar).Append(".WriteFixedLengthBytes(").Append(tag).AppendLine(", __gb);");
                sb.Append("                        }"); return;
            case WireKind.Decimal:
                sb.AppendLine("{");
                sb.Append("                            global::System.Span<int> __dbits = stackalloc int[4]; global::System.Decimal.GetBits(").Append(valueExpr).AppendLine(", __dbits);");
                sb.AppendLine("                            global::System.Span<byte> __db = stackalloc byte[16];");
                sb.AppendLine("                            global::System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(__db.Slice(0, 4), __dbits[0]);");
                sb.AppendLine("                            global::System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(__db.Slice(4, 4), __dbits[1]);");
                sb.AppendLine("                            global::System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(__db.Slice(8, 4), __dbits[2]);");
                sb.AppendLine("                            global::System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(__db.Slice(12, 4), __dbits[3]);");
                sb.Append("                            ").Append(writerVar).Append(".WriteFixedLengthBytes(").Append(tag).AppendLine(", __db);");
                sb.Append("                        }"); return;
            case WireKind.Message:
                // Always write the value field (tag + length); a null message becomes a zero-length
                // message, matching EmitMapValueSize's __valSz for null. Written directly — no temp buffer.
                sb.Append("{ var __dmv = ").Append(valueExpr).Append("; ").Append(writerVar)
                  .Append(".WriteRawTag(").Append(tag).Append(", global::Lite.Serialization.Protobuf.WireFormat.ProtoWireType.LengthDelimited); ")
                  .Append("if (__dmv is null) ").Append(writerVar).Append(".WriteRawVarint(0); else { int __dmvs = ")
                  .Append(nestedSerializerFqn).Append(".ComputeSize(__dmv); ").Append(writerVar).Append(".WriteRawVarint((ulong)__dmvs); ")
                  .Append(writerVar).Append(".Advance(").Append(nestedSerializerFqn).Append(".WriteToSpan(in __dmv, ").Append(writerVar).Append(".FreeSpan)); } }"); return;
            default:
                sb.Append("/* unsupported map kv ").Append(kind).Append(" */"); return;
        }
    }

    private static bool IsPackable(WireKind k) => k switch
    {
        WireKind.Int32 or WireKind.Int64 or WireKind.UInt32 or WireKind.UInt64 or WireKind.Bool or WireKind.Enum
            or WireKind.Int32WidenFromSByte or WireKind.Int32WidenFromShort or WireKind.UInt32WidenFromByte or WireKind.UInt32WidenFromUShort
            or WireKind.Float or WireKind.Double => true,
        _ => false,
    };

    private static string PackedItemSizeExpr(WireKind k, string vexpr) => k switch
    {
        WireKind.Int32 or WireKind.Int32WidenFromSByte or WireKind.Int32WidenFromShort
            => $"global::Lite.Serialization.Protobuf.WireFormat.ProtoWriter.VarintSize((ulong)(long)(int){vexpr})",
        WireKind.Int64 => $"global::Lite.Serialization.Protobuf.WireFormat.ProtoWriter.VarintSize((ulong){vexpr})",
        WireKind.UInt32 or WireKind.UInt32WidenFromByte or WireKind.UInt32WidenFromUShort
            => $"global::Lite.Serialization.Protobuf.WireFormat.ProtoWriter.VarintSize((uint){vexpr})",
        WireKind.UInt64 => $"global::Lite.Serialization.Protobuf.WireFormat.ProtoWriter.VarintSize({vexpr})",
        WireKind.Bool => "1",
        WireKind.Enum => $"global::Lite.Serialization.Protobuf.WireFormat.ProtoWriter.VarintSize((ulong)(long)(int){vexpr})",
        WireKind.Float => "4",
        WireKind.Double => "8",
        _ => "0",
    };

    private static string PackedWriteItemExpr(WireKind k, string vexpr) => k switch
    {
        WireKind.Int32 or WireKind.Int32WidenFromSByte or WireKind.Int32WidenFromShort
            => $"w.WriteRawVarint((ulong)(long)(int){vexpr})",
        WireKind.Int64 => $"w.WriteRawVarint((ulong){vexpr})",
        WireKind.UInt32 or WireKind.UInt32WidenFromByte or WireKind.UInt32WidenFromUShort
            => $"w.WriteRawVarint((uint){vexpr})",
        WireKind.UInt64 => $"w.WriteRawVarint({vexpr})",
        WireKind.Bool => $"w.WriteRawVarint({vexpr} ? 1u : 0u)",
        WireKind.Enum => $"w.WriteRawVarint((ulong)(long)(int){vexpr})",
        WireKind.Float => $"w.WriteRawFixed32(global::System.BitConverter.SingleToUInt32Bits({vexpr}))",
        WireKind.Double => $"w.WriteRawFixed64(global::System.BitConverter.DoubleToUInt64Bits({vexpr}))",
        _ => "/* unsupported */",
    };

    private static void EmitWriteRepeatedItem(StringBuilder sb, WireKind k, int tag, string vexpr, WireMapping mapping)
    {
        switch (k)
        {
            case WireKind.String: sb.Append("if (").Append(vexpr).Append(" is not null) w.WriteString(").Append(tag).Append(", ").Append(vexpr).Append(");"); return;
            case WireKind.Bytes: sb.Append("if (").Append(vexpr).Append(" is not null) w.WriteBytes(").Append(tag).Append(", ").Append(vexpr).Append(");"); return;
            case WireKind.Message:
                sb.AppendLine();
                AppendDirectMessageWrite(sb, "                    ", "w", tag, vexpr, mapping.NestedSerializerFqn!);
                return;
            case WireKind.Guid:
                sb.AppendLine("{");
                sb.AppendLine("                    global::System.Span<byte> __gb = stackalloc byte[16];");
                sb.Append("                    ").Append(vexpr).AppendLine(".TryWriteBytes(__gb);");
                sb.Append("                    w.WriteFixedLengthBytes(").Append(tag).AppendLine(", __gb);");
                sb.Append("                }");
                return;
            case WireKind.DateTime: sb.Append("w.WriteInt64(").Append(tag).Append(", ").Append(vexpr).Append(".ToUniversalTime().Ticks);"); return;
            case WireKind.Decimal:
                sb.AppendLine("{");
                sb.Append("                    global::System.Span<int> __dbits = stackalloc int[4]; global::System.Decimal.GetBits(").Append(vexpr).AppendLine(", __dbits);");
                sb.AppendLine("                    global::System.Span<byte> __db = stackalloc byte[16];");
                sb.AppendLine("                    global::System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(__db.Slice(0, 4), __dbits[0]);");
                sb.AppendLine("                    global::System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(__db.Slice(4, 4), __dbits[1]);");
                sb.AppendLine("                    global::System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(__db.Slice(8, 4), __dbits[2]);");
                sb.AppendLine("                    global::System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(__db.Slice(12, 4), __dbits[3]);");
                sb.Append("                    w.WriteFixedLengthBytes(").Append(tag).AppendLine(", __db);");
                sb.Append("                }");
                return;
            default:
                sb.Append("/* TODO unpacked ").Append(k).Append(" */");
                return;
        }
    }

    private static string DefaultSkipCondition(WireKind k, string member) => k switch
    {
        WireKind.Bool => member,
        WireKind.Int32 or WireKind.UInt32 or WireKind.Int64 or WireKind.UInt64
            or WireKind.Int32WidenFromSByte or WireKind.UInt32WidenFromByte or WireKind.Int32WidenFromShort or WireKind.UInt32WidenFromUShort
            => $"{member} != 0",
        WireKind.Float or WireKind.Double => $"{member} != 0",
        _ => "true",
    };

    private static void EmitWriteScalar(StringBuilder sb, WireKind k, int tag, string valueExpr, WireMapping mapping)
    {
        switch (k)
        {
            case WireKind.Int32: sb.Append("w.WriteInt32(").Append(tag).Append(", ").Append(valueExpr).Append(");"); break;
            case WireKind.Int64: sb.Append("w.WriteInt64(").Append(tag).Append(", ").Append(valueExpr).Append(");"); break;
            case WireKind.UInt32: sb.Append("w.WriteUInt32(").Append(tag).Append(", ").Append(valueExpr).Append(");"); break;
            case WireKind.UInt64: sb.Append("w.WriteUInt64(").Append(tag).Append(", ").Append(valueExpr).Append(");"); break;
            case WireKind.Bool: sb.Append("w.WriteBool(").Append(tag).Append(", ").Append(valueExpr).Append(");"); break;
            case WireKind.Float: sb.Append("w.WriteFloat(").Append(tag).Append(", ").Append(valueExpr).Append(");"); break;
            case WireKind.Double: sb.Append("w.WriteDouble(").Append(tag).Append(", ").Append(valueExpr).Append(");"); break;
            case WireKind.String: sb.Append("w.WriteString(").Append(tag).Append(", ").Append(valueExpr).Append(");"); break;
            case WireKind.Bytes: sb.Append("w.WriteBytes(").Append(tag).Append(", ").Append(valueExpr).Append(");"); break;
            case WireKind.Int32WidenFromSByte:
            case WireKind.Int32WidenFromShort:
                sb.Append("w.WriteInt32(").Append(tag).Append(", (int)").Append(valueExpr).Append(");"); break;
            case WireKind.UInt32WidenFromByte:
            case WireKind.UInt32WidenFromUShort:
                sb.Append("w.WriteUInt32(").Append(tag).Append(", (uint)").Append(valueExpr).Append(");"); break;
            case WireKind.Enum:
                sb.Append("w.WriteInt32(").Append(tag).Append(", (int)").Append(valueExpr).Append(");"); break;
            case WireKind.DateTime:
                sb.Append("w.WriteInt64(").Append(tag).Append(", ").Append(valueExpr).Append(".ToUniversalTime().Ticks);"); break;
            default:
                sb.Append("/* unsupported writer for ").Append(k).Append(" */"); break;
        }
    }

    private static void EmitDeclareLocal(StringBuilder sb, FieldModel f)
    {
        var local = LocalNameFor(f);

        if (f.Mapping.IsMap)
        {
            sb.Append("        global::System.Collections.Generic.Dictionary<")
              .Append(f.Mapping.KeyClrFqn).Append(", ").Append(f.Mapping.ValueClrFqn).Append(">? ")
              .Append(local).AppendLine(" = null;");
            return;
        }

        if (f.Mapping.IsRepeated)
        {
            var elem = f.Mapping.ElementClrTypeFqn!;
            sb.Append("        global::System.Collections.Generic.List<").Append(elem).Append(">? ").Append(local).AppendLine(" = null;");
            return;
        }

        var fqn = f.Mapping.ClrTypeFqn;
        if (f.Mapping.IsOptional)
            sb.Append("        ").Append(fqn).Append(" ").Append(local).AppendLine(" = null;");
        else if (f.Mapping.Kind == WireKind.String)
            sb.Append("        string ").Append(local).AppendLine(" = string.Empty;");
        else if (f.Mapping.Kind == WireKind.Bytes)
            sb.Append("        byte[] ").Append(local).AppendLine(" = global::System.Array.Empty<byte>();");
        else if (f.Mapping.Kind == WireKind.Message)
            sb.Append("        ").Append(fqn).Append("? ").Append(local).AppendLine(" = null;");
        else
            sb.Append("        ").Append(fqn).Append(" ").Append(local).AppendLine(" = default!;");
    }

    private static void EmitReadCase(StringBuilder sb, FieldModel f)
    {
        var local = LocalNameFor(f);
        var k = f.Mapping.Kind;

        if (f.Mapping.IsMap)
        {
            EmitReadMap(sb, f, local);
            return;
        }

        if (f.Mapping.IsRepeated)
        {
            var elem = f.Mapping.ElementClrTypeFqn!;
            sb.Append("                case ").Append(f.Tag).AppendLine(":");
            sb.AppendLine("                {");
            var listCtor = "new global::System.Collections.Generic.List<" + elem + ">";

            if (IsPackable(k))
            {
                // For fixed-width packed items (float/double) the element count is exactly
                // payloadLength / itemSize, so we pre-size the list and avoid every growth realloc.
                string? capExpr = k switch
                {
                    WireKind.Float => "__len / 4",
                    WireKind.Double => "__len / 8",
                    _ => null,
                };
                sb.AppendLine("                    if (wireType == global::Lite.Serialization.Protobuf.WireFormat.ProtoWireType.LengthDelimited)");
                sb.AppendLine("                    {");
                sb.AppendLine("                        var __len = (int)r.ReadRawVarint();");
                sb.AppendLine("                        var __end = r.BytesConsumed + __len;");
                if (capExpr is not null)
                    sb.Append("                        ").Append(local).Append(" ??= ").Append(listCtor).Append("(").Append(capExpr).AppendLine(");");
                else
                    sb.Append("                        ").Append(local).Append(" ??= ").Append(listCtor).AppendLine("();");
                sb.AppendLine("                        while (r.BytesConsumed < __end) " + ReadOneItem(k, local, f.Mapping));
                sb.AppendLine("                    }");
                sb.AppendLine("                    else");
                sb.AppendLine("                    {");
                sb.Append("                        ").Append(local).Append(" ??= ").Append(listCtor).AppendLine("();");
                sb.AppendLine("                        " + ReadOneItem(k, local, f.Mapping));
                sb.AppendLine("                    }");
            }
            else
            {
                sb.Append("                    ").Append(local).Append(" ??= ").Append(listCtor).AppendLine("();");
                sb.AppendLine("                    " + ReadOneItem(k, local, f.Mapping));
            }
            sb.AppendLine("                    break;");
            sb.AppendLine("                }");
            return;
        }

        sb.Append("                case ").Append(f.Tag).Append(":");
        EmitReadOne(sb, f, local);
        sb.AppendLine();
    }

    private static void EmitReadMap(StringBuilder sb, FieldModel f, string local)
    {
        var keyFqn = f.Mapping.KeyClrFqn!;
        var valFqn = f.Mapping.ValueClrFqn!;
        sb.Append("                case ").Append(f.Tag).AppendLine(":");
        sb.AppendLine("                {");
        sb.Append("                    ").Append(local).Append(" ??= new global::System.Collections.Generic.Dictionary<").Append(keyFqn).Append(", ").Append(valFqn).AppendLine(">();");
        sb.AppendLine("                    var __slice = r.ReadLengthDelimitedSpan();");
        sb.AppendLine("                    var __er = new global::Lite.Serialization.Protobuf.WireFormat.SpanProtoReader(__slice);");
        sb.Append("                    ").Append(keyFqn).Append(" __k = ").Append(MapKeyDefault(f.Mapping.KeyKind, keyFqn)).AppendLine(";");
        sb.Append("                    ").Append(valFqn).Append(" __v = ").Append(MapValueDefault(f.Mapping.ValueKind, valFqn)).AppendLine(";");
        sb.AppendLine("                    while (__er.TryReadTag(out var __fn, out var __wt))");
        sb.AppendLine("                    {");
        sb.AppendLine("                        switch (__fn)");
        sb.AppendLine("                        {");
        sb.AppendLine("                            case 1: " + MapReadKvExpr("__er", f.Mapping.KeyKind, "__k", keyFqn, null) + " break;");
        sb.AppendLine("                            case 2: " + MapReadKvExpr("__er", f.Mapping.ValueKind, "__v", valFqn, f.Mapping.ValueNestedSerializerFqn) + " break;");
        sb.AppendLine("                            default: __er.SkipField(__wt); break;");
        sb.AppendLine("                        }");
        sb.AppendLine("                    }");
        sb.Append("                    ").Append(local).AppendLine("[__k] = __v;");
        sb.AppendLine("                    break;");
        sb.AppendLine("                }");
    }

    private static string MapKeyDefault(WireKind k, string fqn) => k switch
    {
        WireKind.String => "string.Empty",
        WireKind.Bool => "false",
        _ => $"default({fqn})",
    };

    private static string MapValueDefault(WireKind k, string fqn) => k switch
    {
        WireKind.String => "string.Empty",
        WireKind.Bool => "false",
        WireKind.Bytes => "global::System.Array.Empty<byte>()",
        WireKind.Message => "default!",
        _ => $"default({fqn})",
    };

    private static string MapReadKvExpr(string readerVar, WireKind kind, string targetVar, string fqn, string? nestedSerializerFqn) => kind switch
    {
        WireKind.Int32 => $"{targetVar} = {readerVar}.ReadInt32();",
        WireKind.Int64 => $"{targetVar} = {readerVar}.ReadInt64();",
        WireKind.UInt32 => $"{targetVar} = {readerVar}.ReadUInt32();",
        WireKind.UInt64 => $"{targetVar} = {readerVar}.ReadUInt64();",
        WireKind.Bool => $"{targetVar} = {readerVar}.ReadBool();",
        WireKind.Float => $"{targetVar} = {readerVar}.ReadFloat();",
        WireKind.Double => $"{targetVar} = {readerVar}.ReadDouble();",
        WireKind.String => $"{targetVar} = {readerVar}.ReadString();",
        WireKind.Bytes => $"{targetVar} = {readerVar}.ReadBytes();",
        WireKind.Enum => $"{targetVar} = ({fqn}){readerVar}.ReadInt32();",
        WireKind.Int32WidenFromSByte => $"{targetVar} = (sbyte){readerVar}.ReadInt32();",
        WireKind.Int32WidenFromShort => $"{targetVar} = (short){readerVar}.ReadInt32();",
        WireKind.UInt32WidenFromByte => $"{targetVar} = (byte){readerVar}.ReadUInt32();",
        WireKind.UInt32WidenFromUShort => $"{targetVar} = (ushort){readerVar}.ReadUInt32();",
        WireKind.DateTime => $"{targetVar} = new global::System.DateTime({readerVar}.ReadInt64(), global::System.DateTimeKind.Utc);",
        WireKind.Guid => $"{{ global::System.Span<byte> __gb = stackalloc byte[16]; {readerVar}.ReadFixedLengthBytes(__gb); {targetVar} = new global::System.Guid(__gb); }}",
        WireKind.Decimal => $"{{ global::System.Span<byte> __db = stackalloc byte[16]; {readerVar}.ReadFixedLengthBytes(__db); {targetVar} = global::Lite.Serialization.Protobuf.Generated.DecimalHelper.DecimalFromBytes(__db); }}",
        WireKind.Message => $"{targetVar} = {nestedSerializerFqn}.ReadFromSpan({readerVar}.ReadLengthDelimitedSpan());",
        _ => "/* unsupported map read */",
    };

    private static string ReadOneItem(WireKind k, string list, WireMapping mapping) => k switch
    {
        WireKind.Int32 => $"{list}.Add(r.ReadInt32());",
        WireKind.Int64 => $"{list}.Add(r.ReadInt64());",
        WireKind.UInt32 => $"{list}.Add(r.ReadUInt32());",
        WireKind.UInt64 => $"{list}.Add(r.ReadUInt64());",
        WireKind.Bool => $"{list}.Add(r.ReadBool());",
        WireKind.Float => $"{list}.Add(r.ReadFloat());",
        WireKind.Double => $"{list}.Add(r.ReadDouble());",
        WireKind.Int32WidenFromSByte => $"{list}.Add((sbyte)r.ReadInt32());",
        WireKind.Int32WidenFromShort => $"{list}.Add((short)r.ReadInt32());",
        WireKind.UInt32WidenFromByte => $"{list}.Add((byte)r.ReadUInt32());",
        WireKind.UInt32WidenFromUShort => $"{list}.Add((ushort)r.ReadUInt32());",
        WireKind.Enum => $"{list}.Add(({mapping.ClrTypeFqn})r.ReadInt32());",
        WireKind.String => $"{list}.Add(r.ReadString());",
        WireKind.Bytes => $"{list}.Add(r.ReadBytes());",
        WireKind.Message => $"{list}.Add({mapping.NestedSerializerFqn}.ReadFromSpan(r.ReadLengthDelimitedSpan()));",
        WireKind.Guid => $"{{ global::System.Span<byte> __gb = stackalloc byte[16]; r.ReadFixedLengthBytes(__gb); {list}.Add(new global::System.Guid(__gb)); }}",
        WireKind.DateTime => $"{list}.Add(new global::System.DateTime(r.ReadInt64(), global::System.DateTimeKind.Utc));",
        WireKind.Decimal => $"{{ global::System.Span<byte> __db = stackalloc byte[16]; r.ReadFixedLengthBytes(__db); {list}.Add(global::Lite.Serialization.Protobuf.Generated.DecimalHelper.DecimalFromBytes(__db)); }}",
        _ => "/* unsupported read */",
    };

    private static void EmitReadOne(StringBuilder sb, FieldModel f, string local)
    {
        var k = f.Mapping.Kind;
        var fqn = f.Mapping.ClrTypeFqn;

        if (f.Mapping.IsOptional)
        {
            var inner = f.Mapping.InnerNonNullableFqn ?? "object";
            switch (k)
            {
                case WireKind.Enum: sb.Append(" ").Append(local).Append(" = (").Append(inner).Append(")r.ReadInt32(); break;"); return;
                case WireKind.Guid:
                    sb.AppendLine();
                    sb.AppendLine("                {");
                    sb.AppendLine("                    global::System.Span<byte> __gb = stackalloc byte[16]; r.ReadFixedLengthBytes(__gb);");
                    sb.Append("                    ").Append(local).Append(" = new global::System.Guid(__gb); break;").AppendLine();
                    sb.Append("                }"); return;
                case WireKind.DateTime: sb.Append(" ").Append(local).Append(" = new global::System.DateTime(r.ReadInt64(), global::System.DateTimeKind.Utc); break;"); return;
                case WireKind.Decimal:
                    sb.AppendLine();
                    sb.AppendLine("                {");
                    sb.AppendLine("                    global::System.Span<byte> __db = stackalloc byte[16]; r.ReadFixedLengthBytes(__db);");
                    sb.Append("                    ").Append(local).Append(" = global::Lite.Serialization.Protobuf.Generated.DecimalHelper.DecimalFromBytes(__db); break;").AppendLine();
                    sb.Append("                }"); return;
                default: sb.Append(" ").Append(local).Append(" = r.").Append(ReaderMethod(k)).Append("(); break;"); return;
            }
        }

        switch (k)
        {
            case WireKind.Message: sb.Append(" ").Append(local).Append(" = ").Append(f.Mapping.NestedSerializerFqn).Append(".ReadFromSpan(r.ReadLengthDelimitedSpan()); break;"); return;
            case WireKind.Enum: sb.Append(" ").Append(local).Append(" = (").Append(fqn).Append(")r.ReadInt32(); break;"); return;
            case WireKind.Guid:
                sb.AppendLine();
                sb.AppendLine("                {");
                sb.AppendLine("                    global::System.Span<byte> __gb = stackalloc byte[16]; r.ReadFixedLengthBytes(__gb);");
                sb.Append("                    ").Append(local).AppendLine(" = new global::System.Guid(__gb); break;");
                sb.Append("                }"); return;
            case WireKind.DateTime: sb.Append(" ").Append(local).Append(" = new global::System.DateTime(r.ReadInt64(), global::System.DateTimeKind.Utc); break;"); return;
            case WireKind.Decimal:
                sb.AppendLine();
                sb.AppendLine("                {");
                sb.AppendLine("                    global::System.Span<byte> __db = stackalloc byte[16]; r.ReadFixedLengthBytes(__db);");
                sb.Append("                    ").Append(local).AppendLine(" = global::Lite.Serialization.Protobuf.Generated.DecimalHelper.DecimalFromBytes(__db); break;");
                sb.Append("                }"); return;
            case WireKind.Int32WidenFromSByte: sb.Append(" ").Append(local).Append(" = (sbyte)r.ReadInt32(); break;"); return;
            case WireKind.Int32WidenFromShort: sb.Append(" ").Append(local).Append(" = (short)r.ReadInt32(); break;"); return;
            case WireKind.UInt32WidenFromByte: sb.Append(" ").Append(local).Append(" = (byte)r.ReadUInt32(); break;"); return;
            case WireKind.UInt32WidenFromUShort: sb.Append(" ").Append(local).Append(" = (ushort)r.ReadUInt32(); break;"); return;
        }

        sb.Append(" ").Append(local).Append(" = r.").Append(ReaderMethod(k)).Append("(); break;");
    }

    private static string ReaderMethod(WireKind k) => k switch
    {
        WireKind.Int32 => "ReadInt32",
        WireKind.Int64 => "ReadInt64",
        WireKind.UInt32 => "ReadUInt32",
        WireKind.UInt64 => "ReadUInt64",
        WireKind.Bool => "ReadBool",
        WireKind.Float => "ReadFloat",
        WireKind.Double => "ReadDouble",
        WireKind.String => "ReadString",
        WireKind.Bytes => "ReadBytes",
        _ => "ReadInt32",
    };

    private static void EmitConstruct(StringBuilder sb, TypeModel m)
    {
        switch (m.Construction.Mode)
        {
            case ConstructionMode.MutableSetter:
                sb.Append("        ").Append(m.TypeFqn).AppendLine(" result = new();");
                foreach (var f in m.Fields)
                    EmitAssignField(sb, f, "result.");
                sb.AppendLine("        return result;");
                return;
            case ConstructionMode.CtorOnly:
            case ConstructionMode.CtorPlusSetters:
                {
                    var ctorArgs = m.Construction.CtorParams.Select(name => MaterializeForAssign(m.Fields.First(f => f.MemberName == name)));
                    sb.Append("        ").Append(m.TypeFqn).Append(" result = new ").Append(m.TypeFqn).Append("(").Append(string.Join(", ", ctorArgs)).AppendLine(");");
                    var ctorSet = new HashSet<string>(m.Construction.CtorParams, StringComparer.Ordinal);
                    foreach (var f in m.Fields)
                    {
                        if (ctorSet.Contains(f.MemberName)) continue;
                        EmitAssignField(sb, f, "result.");
                    }
                    sb.AppendLine("        return result;");
                    return;
                }
        }
    }

    private static void EmitAssignField(StringBuilder sb, FieldModel f, string targetPrefix)
    {
        sb.Append("        ").Append(targetPrefix).Append(f.MemberName).Append(" = ").Append(MaterializeForAssign(f)).AppendLine(";");
    }

    private static string MaterializeForAssign(FieldModel f)
    {
        var local = LocalNameFor(f);
        if (f.Mapping.IsMap)
        {
            var k = f.Mapping.KeyClrFqn;
            var v = f.Mapping.ValueClrFqn;
            return $"{local} ?? new global::System.Collections.Generic.Dictionary<{k}, {v}>()";
        }
        if (f.Mapping.IsRepeated)
        {
            var elem = f.Mapping.ElementClrTypeFqn!;
            return f.Mapping.CollectionConcrete switch
            {
                "Array" => $"{local}?.ToArray() ?? global::System.Array.Empty<{elem}>()",
                _ => $"{local} ?? new global::System.Collections.Generic.List<{elem}>()",
            };
        }
        return local;
    }

    private static string LocalNameFor(FieldModel f) =>
        "__f_" + new string(f.MemberName.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());

    // ----- Emit interceptors -----
    private static void EmitInterceptors(SourceProductionContext spc, ImmutableArray<CallSite> sites, IReadOnlyDictionary<string, TypeModel> typeModels)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("namespace Lite.Serialization.Protobuf.Generated;");
        sb.AppendLine();
        sb.AppendLine("file static class __ForInterceptors");
        sb.AppendLine("{");
        var idx = 0;
        foreach (var site in sites)
        {
            if (!typeModels.TryGetValue(site.TargetFqn, out var model)) continue;
            var serializerFqn = $"{(string.IsNullOrEmpty(model.Namespace) ? "" : model.Namespace + ".")}{model.TypeName}__ProtoSerializer";

            sb.Append("    [global::System.Runtime.CompilerServices.InterceptsLocation(")
              .Append(site.InterceptVersion).Append(", \"").Append(site.InterceptData).AppendLine("\")]");

            switch (site.MethodName)
            {
                case "MarshallerFor":
                    sb.Append("    public static global::Grpc.Core.Marshaller<").Append(site.TargetFqn).Append("> __MFor_").Append(idx++).AppendLine("()");
                    sb.Append("        => global::").Append(serializerFqn).AppendLine(".Marshaller;");
                    break;
                case "SerializeToWriter":
                    sb.Append("    public static void __SerW_").Append(idx++).Append("(in ").Append(site.TargetFqn).AppendLine(" value, global::System.Buffers.IBufferWriter<byte> writer)");
                    sb.Append("        => global::").Append(serializerFqn).AppendLine(".WriteTo(in value, writer);");
                    break;
                case "SerializeTo":
                    sb.Append("    public static int __SerTo_").Append(idx++).Append("(in ").Append(site.TargetFqn).AppendLine(" value, global::System.Span<byte> destination)");
                    sb.Append("        => global::").Append(serializerFqn).AppendLine(".WriteToSpan(in value, destination);");
                    break;
                case "ComputeSize":
                    sb.Append("    public static int __Sz_").Append(idx++).Append("(in ").Append(site.TargetFqn).AppendLine(" value)");
                    sb.Append("        => global::").Append(serializerFqn).AppendLine(".ComputeSize(value);");
                    break;
                case "SerializeRented":
                    sb.Append("    public static global::Lite.Serialization.Protobuf.RentedBuffer __SerRent_").Append(idx++).Append("(in ").Append(site.TargetFqn).AppendLine(" value)");
                    sb.AppendLine("    {");
                    sb.Append("        var __sz = global::").Append(serializerFqn).AppendLine(".ComputeSize(value);");
                    sb.AppendLine("        var __owner = global::System.Buffers.MemoryPool<byte>.Shared.Rent(__sz);");
                    sb.Append("        var __wrote = global::").Append(serializerFqn).AppendLine(".WriteToSpan(in value, __owner.Memory.Span);");
                    sb.AppendLine("        return new global::Lite.Serialization.Protobuf.RentedBuffer(__owner, __wrote);");
                    sb.AppendLine("    }");
                    break;
                case "DeserializeFromSeq":
                    sb.Append("    public static ").Append(site.TargetFqn).Append(" __DesSeq_").Append(idx++).AppendLine("(global::System.Buffers.ReadOnlySequence<byte> source)");
                    sb.Append("        => global::").Append(serializerFqn).AppendLine(".ReadFrom(source);");
                    break;
                case "DeserializeFromSpan":
                    sb.Append("    public static ").Append(site.TargetFqn).Append(" __DesSpan_").Append(idx++).AppendLine("(global::System.ReadOnlySpan<byte> source)");
                    sb.Append("        => global::").Append(serializerFqn).AppendLine(".ReadFromSpan(source);");
                    break;
                default: // For
                    sb.Append("    public static global::Lite.Serialization.Protobuf.IProtoSerializer<").Append(site.TargetFqn).Append("> __For_").Append(idx++).AppendLine("()");
                    sb.Append("        => global::").Append(serializerFqn).AppendLine(".Instance;");
                    break;
            }
        }
        sb.AppendLine("}");

        spc.AddSource("__ForInterceptors.g.cs", sb.ToString());
    }

    // ----- Emit proto schemas -----
    private static void EmitProtoSchemas(SourceProductionContext spc, IEnumerable<TypeModel> models, IEnumerable<EnumModel> enums)
    {
        var modelList = models.ToList();
        var enumList = enums.ToList();

        var groups = modelList.Select(m => (Pkg: m.ProtoPackage ?? "", Kind: "msg", Name: m.ProtoMessageName, Model: (object)m))
            .Concat(enumList.Select(e => (Pkg: e.ProtoPackage ?? "", Kind: "enum", Name: e.ProtoEnumName, Model: (object)e)))
            .GroupBy(g => g.Pkg, StringComparer.Ordinal);

        var outer = new StringBuilder();
        outer.AppendLine("// <auto-generated/>");
        outer.AppendLine("#nullable enable");
        outer.AppendLine();

        foreach (var group in groups)
        {
            var pkg = group.Key;
            var fileName = (string.IsNullOrEmpty(pkg) ? "schema" : SafeFileName(pkg)) + ".proto";

            var proto = new StringBuilder();
            proto.AppendLine("syntax = \"proto3\";");
            if (!string.IsNullOrEmpty(pkg)) proto.Append("package ").Append(pkg).AppendLine(";");
            proto.AppendLine();

            foreach (var item in group.OrderBy(x => x.Kind, StringComparer.Ordinal).ThenBy(x => x.Name, StringComparer.Ordinal))
            {
                if (item.Model is EnumModel e)
                {
                    proto.Append("enum ").Append(e.ProtoEnumName).AppendLine(" {");
                    var hasZero = e.Values.Any(v => v.Value == 0);
                    if (!hasZero) proto.Append("  ").Append(e.TypeName.ToUpperInvariant()).AppendLine("_UNSPECIFIED = 0;");
                    foreach (var v in e.Values.OrderBy(v => v.Value))
                        proto.Append("  ").Append(v.Name).Append(" = ").Append(v.Value).AppendLine(";");
                    proto.AppendLine("}");
                    proto.AppendLine();
                }
                else if (item.Model is TypeModel m)
                {
                    proto.Append("message ").Append(m.ProtoMessageName).AppendLine(" {");
                    foreach (var f in m.Fields.OrderBy(x => x.Tag))
                    {
                        var prefix = f.Mapping.IsMap ? "" : (f.Mapping.IsRepeated ? "repeated " : (f.Mapping.IsOptional ? "optional " : ""));
                        proto.Append("  ").Append(prefix).Append(f.Mapping.ProtoType).Append(' ').Append(f.ProtoName).Append(" = ").Append(f.Tag).AppendLine(";");
                    }
                    proto.AppendLine("}");
                    proto.AppendLine();
                }
            }

            outer.Append("[assembly: global::Lite.Serialization.Protobuf.Attributes.GeneratedProtoSchemaAttribute(")
                 .Append(EscapeStringLiteral(fileName)).Append(", ").Append(EscapeStringLiteral(proto.ToString())).AppendLine(")]");
        }

        spc.AddSource("__GeneratedProtoSchemas.g.cs", outer.ToString());
    }

    // ----- Emit InterceptsLocation polyfill + decimal helper -----
    private static void EmitInterceptsLocationAttribute(SourceProductionContext spc)
    {
        var src = """
// <auto-generated/>
#nullable enable
namespace System.Runtime.CompilerServices
{
    [global::System.AttributeUsage(global::System.AttributeTargets.Method, AllowMultiple = true)]
    internal sealed class InterceptsLocationAttribute : global::System.Attribute
    {
        public InterceptsLocationAttribute(int version, string data) { _ = version; _ = data; }
    }
}

namespace Lite.Serialization.Protobuf.Generated
{
    internal static class DecimalHelper
    {
        public static decimal DecimalFromBytes(global::System.ReadOnlySpan<byte> b)
        {
            global::System.Span<int> bits = stackalloc int[4];
            bits[0] = global::System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(b.Slice(0, 4));
            bits[1] = global::System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(b.Slice(4, 4));
            bits[2] = global::System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(b.Slice(8, 4));
            bits[3] = global::System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(b.Slice(12, 4));
            return new decimal(bits);
        }
    }
}
""";
        spc.AddSource("__InterceptsLocation.g.cs", src);
    }

    // ----- helpers -----
    private static string ToSnakeCase(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        var sb = new StringBuilder(s.Length + 4);
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (i > 0 && char.IsUpper(c) && (char.IsLower(s[i - 1]) || (i + 1 < s.Length && char.IsLower(s[i + 1]))))
                sb.Append('_');
            sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }

    private static string SafeFileName(string s)
    {
        if (string.IsNullOrEmpty(s)) return "schema";
        var chars = s.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
            if (!char.IsLetterOrDigit(chars[i]) && chars[i] != '.' && chars[i] != '_' && chars[i] != '-')
                chars[i] = '_';
        return new string(chars);
    }

    private static string EscapeStringLiteral(string s)
    {
        var sb = new StringBuilder(s.Length + 2);
        sb.Append('"');
        foreach (var c in s)
        {
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '"': sb.Append("\\\""); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default: sb.Append(c); break;
            }
        }
        sb.Append('"');
        return sb.ToString();
    }

    // ----- Diagnostics -----
    private static readonly DiagnosticDescriptor UnsupportedType = new(
        id: "LITEGRPC001", title: "Unsupported member type",
        messageFormat: "Type '{0}' on member '{1}' of '{2}' is not supported by Lite.Serialization.Protobuf",
        category: "LiteGrpc", defaultSeverity: DiagnosticSeverity.Error, isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor NotConstructible = new(
        id: "LITEGRPC002", title: "Type cannot be constructed for deserialization",
        messageFormat: "Cannot construct '{1}': member '{0}' is not settable and not part of any constructor parameter list",
        category: "LiteGrpc", defaultSeverity: DiagnosticSeverity.Error, isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor DuplicateTag = new(
        id: "LITEGRPC003", title: "Duplicate proto tag",
        messageFormat: "Duplicate proto tag {0} in '{1}' between {2}; override one via IProtoSerializerConfiguration<T>",
        category: "LiteGrpc", defaultSeverity: DiagnosticSeverity.Error, isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor GenericNotSupported = new(
        id: "LITEGRPC004", title: "Generic types not supported",
        messageFormat: "Generic type '{0}' is not supported as a serialization root in V1",
        category: "LiteGrpc", defaultSeverity: DiagnosticSeverity.Warning, isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor NoMembers = new(
        id: "LITEGRPC005", title: "No serializable members",
        messageFormat: "Type '{0}' has no public properties or fields to serialize",
        category: "LiteGrpc", defaultSeverity: DiagnosticSeverity.Warning, isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor GeneratorCrashed = new(
        id: "LITEGRPC900", title: "Lite.Serialization.Protobuf generator crashed",
        messageFormat: "{0}: {1}",
        category: "LiteGrpc", defaultSeverity: DiagnosticSeverity.Error, isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor GrpcInteropSkipped = new(
        id: "LITEGRPC100", title: "gRPC marshaller interception skipped",
        messageFormat: "Message '{0}' has a shape Lite cannot serialize yet (repeated/map/oneof); its Marshallers.Create call is left to Google.Protobuf (wire format stays compatible)",
        category: "LiteGrpc", defaultSeverity: DiagnosticSeverity.Info, isEnabledByDefault: true);
}

internal readonly record struct CallSite(string TargetFqn, int InterceptVersion, string InterceptData, string MethodName);

internal readonly record struct GrpcMarshallerSite(string TargetFqn, int InterceptVersion, string InterceptData, string ParamTypes);

internal sealed record ConfigInfo(string TargetFqn, ImmutableArray<FieldOverride> Overrides);

internal readonly record struct FieldOverride(string PropertyName, int? Tag, string? Name, bool Ignore);

internal sealed record TypeModel(
    string Namespace,
    string TypeName,
    string TypeFqn,
    string ProtoMessageName,
    string ProtoPackage,
    bool IsValueType,
    bool IsRecord,
    IReadOnlyList<FieldModel> Fields,
    ConstructionStrategy Construction);

internal sealed record EnumModel(
    string Namespace,
    string TypeName,
    string TypeFqn,
    string ProtoEnumName,
    string ProtoPackage,
    IReadOnlyList<EnumValueModel> Values);

internal readonly record struct EnumValueModel(string Name, int Value);

internal sealed record FieldModel(string MemberName, string ProtoName, int Tag, MemberInfo Member, WireMapping Mapping);

internal sealed record MemberInfo(string Name, ITypeSymbol Type, bool IsSettable, bool IsInitOnly, bool IsField);

internal readonly record struct WireMapping(
    WireKind Kind,
    string ProtoType,
    string ClrTypeFqn,
    bool IsOptional = false,
    string? InnerNonNullableFqn = null,
    bool IsRepeated = false,
    string? ElementClrTypeFqn = null,
    string? CollectionConcrete = null,
    string? NestedSerializerFqn = null,
    string? EnumUnderlyingFqn = null,
    bool IsMap = false,
    WireKind KeyKind = default,
    string? KeyClrFqn = null,
    string? KeyProtoType = null,
    WireKind ValueKind = default,
    string? ValueClrFqn = null,
    string? ValueProtoType = null,
    string? ValueNestedSerializerFqn = null,
    string? ValueEnumUnderlyingFqn = null,
    string? DictionaryConcrete = null);

internal enum WireKind
{
    Int32, Int64, UInt32, UInt64,
    Bool,
    Float, Double,
    String, Bytes,
    Int32WidenFromSByte, Int32WidenFromShort,
    UInt32WidenFromByte, UInt32WidenFromUShort,
    Message,
    Enum,
    Guid,
    DateTime,
    Decimal,
}

internal readonly record struct ConstructionStrategy(ConstructionMode Mode, IReadOnlyList<string> CtorParams);

internal enum ConstructionMode { MutableSetter, CtorOnly, CtorPlusSetters }
