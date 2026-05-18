using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Lite.Serialization.Protobuf.Cli;

internal sealed class ImportOptions
{
    public string? Namespace { get; set; }
    public int StructThreshold { get; set; } = 32;
    public bool AllClass { get; set; }
    public bool AllStruct { get; set; }
    public List<string> ForceStruct { get; set; } = new();
    public List<string> ForceClass { get; set; } = new();
}

internal static class ImportCommand
{
    public static int Run(string[] args)
    {
        var protoFiles = new List<string>();
        var outputDir = ".";
        var verbose = false;
        string? configPath = null;
        string? nsOverride = null;
        int? thresholdOverride = null;
        bool? allClass = null;
        bool? allStruct = null;

        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i];
            switch (a)
            {
                case "-o" or "--output":
                    if (++i >= args.Length) { Err("missing value for --output"); return 2; }
                    outputDir = args[i];
                    break;
                case "-c" or "--config":
                    if (++i >= args.Length) { Err("missing value for --config"); return 2; }
                    configPath = args[i];
                    break;
                case "-n" or "--namespace":
                    if (++i >= args.Length) { Err("missing value for --namespace"); return 2; }
                    nsOverride = args[i];
                    break;
                case "--struct-threshold":
                    if (++i >= args.Length || !int.TryParse(args[i], out var th)) { Err("invalid --struct-threshold"); return 2; }
                    thresholdOverride = th;
                    break;
                case "--all-class":
                    allClass = true;
                    break;
                case "--all-struct":
                    allStruct = true;
                    break;
                case "-v" or "--verbose":
                    verbose = true;
                    break;
                case "-h" or "--help":
                    PrintUsage();
                    return 0;
                default:
                    if (a.StartsWith('-')) { Err($"unknown option: {a}"); return 2; }
                    protoFiles.Add(a);
                    break;
            }
        }

        if (protoFiles.Count == 0) { Err("no .proto files specified"); PrintUsage(); return 2; }
        foreach (var f in protoFiles)
            if (!File.Exists(f)) { Err($"file not found: {f}"); return 1; }

        var options = new ImportOptions();
        if (configPath is not null)
        {
            if (!File.Exists(configPath)) { Err($"config not found: {configPath}"); return 1; }
            try
            {
                options = JsonSerializer.Deserialize<ImportOptions>(File.ReadAllText(configPath),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new ImportOptions();
            }
            catch (Exception ex) { Err($"bad config: {ex.Message}"); return 1; }
        }

        if (nsOverride is not null) options.Namespace = nsOverride;
        if (thresholdOverride is not null) options.StructThreshold = thresholdOverride.Value;
        if (allClass is not null) options.AllClass = allClass.Value;
        if (allStruct is not null) options.AllStruct = allStruct.Value;

        if (options.AllClass && options.AllStruct) { Err("--all-class and --all-struct are mutually exclusive"); return 2; }

        Directory.CreateDirectory(outputDir);

        try
        {
            var count = ProtoImporter.Import(protoFiles, outputDir, options, verbose);
            Console.WriteLine($"Generated {count} C# file(s) in {Path.GetFullPath(outputDir)}");
            return 0;
        }
        catch (Exception ex)
        {
            Err(ex.Message);
            if (verbose) Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Usage: lite-proto import <file.proto> [<file2.proto> ...] [options]");
        Console.WriteLine();
        Console.WriteLine("Generates C# message types from .proto files.");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  -o, --output <dir>          Output directory (default: current dir)");
        Console.WriteLine("  -n, --namespace <ns>        C# namespace for generated types");
        Console.WriteLine("  -c, --config <file.json>    Config file (struct/class overrides)");
        Console.WriteLine("      --struct-threshold <n>  Max estimated bytes for struct (default: 32)");
        Console.WriteLine("      --all-class             Force every message to a class");
        Console.WriteLine("      --all-struct            Force every message to a struct");
        Console.WriteLine("  -v, --verbose               Verbose logging");
        Console.WriteLine();
        Console.WriteLine("Config JSON:");
        Console.WriteLine("  { \"namespace\": \"...\", \"structThreshold\": 32,");
        Console.WriteLine("    \"forceStruct\": [\"Point\"], \"forceClass\": [\"BigRequest\"] }");
    }

    private static void Err(string msg) =>
        Console.Error.WriteLine($"lite-proto import: error: {msg}");
}
