using System.Reflection;

namespace Lite.Serialization.Protobuf;

public static class ProtoSchemaRegistry
{
    public static IEnumerable<(string FileName, string Content)> Enumerate(Assembly? assembly = null)
    {
        assembly ??= Assembly.GetCallingAssembly();
        foreach (var attr in assembly.GetCustomAttributes<GeneratedProtoSchemaAttribute>())
            yield return (attr.FileName, attr.Content);
    }

    public static void DumpToDirectory(string directory, Assembly? assembly = null)
    {
        assembly ??= Assembly.GetCallingAssembly();
        Directory.CreateDirectory(directory);
        foreach (var (fileName, content) in Enumerate(assembly))
            File.WriteAllText(Path.Combine(directory, fileName), content);
    }
}
