using System;
using System.Buffers;
using System.Diagnostics;
using Google.Protobuf;
using Lite.Serialization.Protobuf;
using Lite.Serialization.Protobuf.Benchmarks.Proto;

namespace Lite.Serialization.Protobuf.Benchmarks;

// Lightweight in-process micro-bench (Stopwatch + GC allocation counters). NOT a replacement for
// BenchmarkDotNet — but it runs anywhere `dotnet run` runs and reports real ns/op + bytes/op.
// Usage:  dotnet run -c Release -- quick
internal static class ManualBench
{
    private static readonly byte[] _scratch = new byte[64 * 1024];

    public static void Run()
    {
        Console.WriteLine();
        Console.WriteLine("Manual micro-bench — best of 5 passes; allocations via GC.GetAllocatedBytesForCurrentThread.");
        Console.WriteLine("For rigorous stats (3 launches x 20 iters + GC gens) run BenchmarkDotNet: dotnet run -c Release");
        Console.WriteLine();
        Header();

        // ───────── Small ─────────
        var gUser = BenchData.UserGoogle();
        var pUser = BenchData.UserPn();
        var lUser = BenchData.UserClass();
        var gUserB = gUser.ToByteArray();
        var pUserB = Pn.ToBytes(pUser);
        var lUserB = LiteSerializer.For<BenchUserClass>().Serialize(lUser);

        // Competitors return a freshly-allocated byte[]; Lite writes into a caller buffer (0 alloc).
        Section("Small (BenchUser) — Serialize");
        Row("Google.Protobuf -> byte[]", 1_000_000, () => gUser.ToByteArray());
        Row("protobuf-net -> byte[]", 1_000_000, () => Pn.ToBytes(pUser));
        Row("Lite SerializeTo + stackalloc", 1_000_000, () => { Span<byte> __b = stackalloc byte[128]; LiteSerializer.SerializeTo<BenchUserClass>(in lUser, __b); });
        Row("Lite SerializeTo + ArrayPool", 1_000_000, () => { var __a = System.Buffers.ArrayPool<byte>.Shared.Rent(128); LiteSerializer.SerializeTo<BenchUserClass>(in lUser, __a); System.Buffers.ArrayPool<byte>.Shared.Return(__a); });
        Row("Lite SerializeTo + reused buffer", 1_000_000, () => LiteSerializer.SerializeTo<BenchUserClass>(in lUser, _scratch));

        Section("Small (BenchUser) — Deserialize");
        Row("Google.Protobuf (IMessage)", 1_000_000, () => BenchUser.Parser.ParseFrom(gUserB));
        Row("protobuf-net", 1_000_000, () => Pn.From<PnUser>(pUserB));
        Row("Lite (class)", 1_000_000, () => LiteSerializer.DeserializeFrom<BenchUserClass>(new ReadOnlySequence<byte>(lUserB)));

        // ───────── Medium ─────────
        var gMed = BenchData.MediumGoogle();
        var pMed = BenchData.MediumPn();
        var lMed = BenchData.MediumLite();
        var gMedB = gMed.ToByteArray();
        var pMedB = Pn.ToBytes(pMed);
        var lMedB = LiteSerializer.For<BenchMediumClass>().Serialize(lMed);

        Section("Medium (nested + repeated + map) — Serialize");
        Row("Google.Protobuf -> byte[]", 500_000, () => gMed.ToByteArray());
        Row("protobuf-net -> byte[]", 500_000, () => Pn.ToBytes(pMed));
        Row("Lite SerializeTo (reused buf)", 500_000, () => LiteSerializer.SerializeTo<BenchMediumClass>(in lMed, _scratch));

        Section("Medium — Deserialize");
        Row("Google.Protobuf (IMessage)", 500_000, () => BenchMedium.Parser.ParseFrom(gMedB));
        Row("protobuf-net", 500_000, () => Pn.From<PnMedium>(pMedB));
        Row("Lite (class)", 500_000, () => LiteSerializer.DeserializeFrom<BenchMediumClass>(new ReadOnlySequence<byte>(lMedB)));

        // ───────── Large ─────────
        var gLarge = BenchData.LargeGoogle();
        var pLarge = BenchData.LargePn();
        var lLarge = BenchData.LargeLite();
        var gLargeB = gLarge.ToByteArray();
        var pLargeB = Pn.ToBytes(pLarge);
        var lLargeB = LiteSerializer.For<BenchLargeClass>().Serialize(lLarge);

        Section("Large (1000 items) — Serialize");
        Row("Google.Protobuf -> byte[]", 5_000, () => gLarge.ToByteArray());
        Row("protobuf-net -> byte[]", 5_000, () => Pn.ToBytes(pLarge));
        Row("Lite SerializeTo (reused buf)", 5_000, () => LiteSerializer.SerializeTo<BenchLargeClass>(in lLarge, _scratch));

        Section("Large — Deserialize");
        Row("Google.Protobuf (IMessage)", 5_000, () => BenchLarge.Parser.ParseFrom(gLargeB));
        Row("protobuf-net", 5_000, () => Pn.From<PnLarge>(pLargeB));
        Row("Lite (class)", 5_000, () => LiteSerializer.DeserializeFrom<BenchLargeClass>(new ReadOnlySequence<byte>(lLargeB)));

        // ───────── Type kinds (vs IMessage) ─────────
        var sStruct = BenchData.UserStruct();
        var sRecord = BenchData.UserRecord();
        var sRecordStruct = BenchData.UserRecordStruct();
        var sRoRecordStruct = BenchData.UserReadonlyRecordStruct();

        Section("Type kinds — SerializeTo (Small shape; Google IMessage baseline allocates)");
        Row("Google.Protobuf -> byte[]", 1_000_000, () => gUser.ToByteArray());
        Row("Lite class", 1_000_000, () => LiteSerializer.SerializeTo<BenchUserClass>(in lUser, _scratch));
        Row("Lite struct", 1_000_000, () => LiteSerializer.SerializeTo<BenchUserStruct>(in sStruct, _scratch));
        Row("Lite record class", 1_000_000, () => LiteSerializer.SerializeTo<BenchUserRecord>(in sRecord, _scratch));
        Row("Lite record struct", 1_000_000, () => LiteSerializer.SerializeTo<BenchUserRecordStruct>(in sRecordStruct, _scratch));
        Row("Lite readonly record struct", 1_000_000, () => LiteSerializer.SerializeTo<BenchUserReadonlyRecordStruct>(in sRoRecordStruct, _scratch));

        Console.WriteLine();
    }

    private static void Header()
    {
        Console.WriteLine($"  {"Library",-32}{"ns/op",10}{"B/op",10}");
    }

    private static void Section(string title)
    {
        Console.WriteLine();
        Console.WriteLine(title);
        Console.WriteLine(new string('─', title.Length));
    }

    private static void Row(string name, int iters, Action op)
    {
        int warmup = Math.Min(iters, 50_000);
        for (int i = 0; i < warmup; i++) op();

        double bestNs = double.MaxValue;
        long bytesPerOp = 0;
        for (int pass = 0; pass < 5; pass++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            long before = GC.GetAllocatedBytesForCurrentThread();
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < iters; i++) op();
            sw.Stop();
            long after = GC.GetAllocatedBytesForCurrentThread();

            double ns = sw.Elapsed.TotalMilliseconds * 1_000_000.0 / iters;
            if (ns < bestNs) bestNs = ns;
            bytesPerOp = (after - before) / iters;
        }

        Console.WriteLine($"  {name,-32}{bestNs,10:F1}{bytesPerOp,10}");
    }
}
