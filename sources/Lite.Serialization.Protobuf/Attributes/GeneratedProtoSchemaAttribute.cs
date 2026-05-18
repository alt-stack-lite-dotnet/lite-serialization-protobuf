using System;

namespace Lite.Serialization.Protobuf;

[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true, Inherited = false)]
public sealed class GeneratedProtoSchemaAttribute : Attribute
{
    public string FileName { get; }
    public string Content { get; }

    public GeneratedProtoSchemaAttribute(string fileName, string content)
    {
        FileName = fileName;
        Content = content;
    }
}
