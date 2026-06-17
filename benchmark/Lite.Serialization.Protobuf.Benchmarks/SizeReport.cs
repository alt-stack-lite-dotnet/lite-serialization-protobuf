using System;
using Google.Protobuf;
using Lite.Serialization.Protobuf;

namespace Lite.Serialization.Protobuf.Benchmarks;

// Wire-size comparison (no timing). Run with:  dotnet run -c Release -- sizes
internal static class SizeReport
{
    public static void Print()
    {
        Console.WriteLine();
        Console.WriteLine("Wire size in bytes (lower is better)");
        Console.WriteLine("───────────────────────────────────────────────────────────────────────");
        Console.WriteLine($"{"Payload",-26}{"Google",10}{"protobuf-net",16}{"Lite",10}");
        Console.WriteLine("───────────────────────────────────────────────────────────────────────");

        Row("Small (BenchUser)",
            BenchData.UserGoogle().CalculateSize(),
            Pn.ToBytes(BenchData.UserPn()).Length,
            LiteSmall());

        Row("Medium (nested+map)",
            BenchData.MediumGoogle().CalculateSize(),
            Pn.ToBytes(BenchData.MediumPn()).Length,
            LiteMedium());

        Row("Large (1000 items)",
            BenchData.LargeGoogle().CalculateSize(),
            Pn.ToBytes(BenchData.LargePn()).Length,
            LiteLarge());

        Console.WriteLine("───────────────────────────────────────────────────────────────────────");
        Console.WriteLine("All models use explicit field numbers (const XxxFieldNumber), so Lite is");
        Console.WriteLine("byte-for-byte identical to Google.Protobuf. protobuf-net differs slightly");
        Console.WriteLine("on repeated/map framing. Without field numbers Lite falls back to name-hash");
        Console.WriteLine("tags (wider) — fine for storage, but specify field numbers for the wire.");
        Console.WriteLine();
    }

    private static int LiteSmall()
    {
        var v = BenchData.UserClass();
        return LiteSerializer.For<BenchUserClass>().Serialize(v).Length;
    }

    private static int LiteMedium()
    {
        var v = BenchData.MediumLite();
        return LiteSerializer.For<BenchMediumClass>().Serialize(v).Length;
    }

    private static int LiteLarge()
    {
        var v = BenchData.LargeLite();
        return LiteSerializer.For<BenchLargeClass>().Serialize(v).Length;
    }

    private static void Row(string name, int google, int pn, int lite) =>
        Console.WriteLine($"{name,-26}{google,10}{pn,16}{lite,10}");
}
