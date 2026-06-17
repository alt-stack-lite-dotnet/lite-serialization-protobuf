using System.Reflection;
using Lite.Serialization.Protobuf.Attributes;

namespace Lite.Serialization.Protobuf;

public static class ProtoSchemaRegistry
{
    public static IEnumerable<(string FileName, string Content)> Enumerate(Assembly? assembly = null)
    {
        assembly ??= Assembly.GetCallingAssembly();
        foreach (var attr in assembly.GetCustomAttributes<GeneratedProtoSchemaAttribute>())
            yield return (attr.FileName, attr.Content);
    }
}
