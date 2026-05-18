using System;

namespace Lite.Serialization.Protobuf.Cli;

internal static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help")
        {
            PrintUsage();
            return 0;
        }

        var rest = args[1..];
        switch (args[0])
        {
            case "export":
                return ExportCommand.Run(rest);
            case "import":
                return ImportCommand.Run(rest);
            default:
                Console.Error.WriteLine($"lite-proto: error: unknown command '{args[0]}'");
                PrintUsage();
                return 2;
        }
    }

    private static void PrintUsage()
    {
        Console.WriteLine("lite-proto — Lite.Serialization.Protobuf CLI");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  export   Extract .proto schemas embedded in a built assembly into .proto files.");
        Console.WriteLine("  import   Generate C# message types from .proto files.");
        Console.WriteLine();
        Console.WriteLine("Run 'lite-proto <command> --help' for command-specific options.");
    }
}
