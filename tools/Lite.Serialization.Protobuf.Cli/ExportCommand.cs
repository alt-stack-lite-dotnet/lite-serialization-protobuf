using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Lite.Serialization.Protobuf.Cli;

internal static class ExportCommand
{
    private const string AttributeFqn = "Lite.Serialization.Protobuf.GeneratedProtoSchemaAttribute";

    public static int Run(string[] args)
    {
        string? assemblyPath = null;
        var outputDir = ".";
        var verbose = false;

        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i];
            switch (a)
            {
                case "-o" or "--output":
                    if (++i >= args.Length) { Err("missing value for --output"); return 2; }
                    outputDir = args[i];
                    break;
                case "-v" or "--verbose":
                    verbose = true;
                    break;
                case "-h" or "--help":
                    PrintUsage();
                    return 0;
                default:
                    if (a.StartsWith('-')) { Err($"unknown option: {a}"); return 2; }
                    if (assemblyPath is null) assemblyPath = a;
                    else { Err($"unexpected argument: {a}"); return 2; }
                    break;
            }
        }

        if (assemblyPath is null) { Err("missing <assembly.dll>"); PrintUsage(); return 2; }
        if (!File.Exists(assemblyPath)) { Err($"assembly not found: {assemblyPath}"); return 1; }

        Directory.CreateDirectory(outputDir);

        try
        {
            var count = Export(assemblyPath, outputDir, verbose);
            Console.WriteLine($"Wrote {count} .proto file(s) to {Path.GetFullPath(outputDir)}");
            return 0;
        }
        catch (Exception ex)
        {
            Err(ex.Message);
            if (verbose) Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static int Export(string assemblyPath, string outputDir, bool verbose)
    {
        var resolverPaths = new List<string> { assemblyPath };
        resolverPaths.AddRange(Directory.GetFiles(RuntimeEnvironment.GetRuntimeDirectory(), "*.dll"));
        var inputDir = Path.GetDirectoryName(Path.GetFullPath(assemblyPath));
        if (inputDir is not null)
            resolverPaths.AddRange(Directory.GetFiles(inputDir, "*.dll").Where(p => !resolverPaths.Contains(p)));

        var resolver = new PathAssemblyResolver(resolverPaths);
        using var mlc = new MetadataLoadContext(resolver);
        var asm = mlc.LoadFromAssemblyPath(Path.GetFullPath(assemblyPath));

        var written = 0;
        foreach (var attr in asm.GetCustomAttributesData())
        {
            if (attr.AttributeType.FullName != AttributeFqn) continue;
            if (attr.ConstructorArguments.Count < 2) continue;

            var fileName = attr.ConstructorArguments[0].Value as string;
            var content = attr.ConstructorArguments[1].Value as string;
            if (string.IsNullOrEmpty(fileName) || content is null) continue;

            var outPath = Path.Combine(outputDir, Path.GetFileName(fileName));
            File.WriteAllText(outPath, content);
            if (verbose) Console.WriteLine($"  → {outPath} ({content.Length} chars)");
            written++;
        }

        if (written == 0 && verbose)
            Console.WriteLine("(no GeneratedProtoSchema attributes found)");
        return written;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Usage: lite-proto export <assembly.dll> [--output <dir>] [--verbose]");
        Console.WriteLine();
        Console.WriteLine("Extracts .proto schemas embedded by the source generator into .proto files.");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  -o, --output <dir>   Output directory (default: current dir)");
        Console.WriteLine("  -v, --verbose        Verbose logging");
    }

    private static void Err(string msg) =>
        Console.Error.WriteLine($"lite-proto export: error: {msg}");
}
