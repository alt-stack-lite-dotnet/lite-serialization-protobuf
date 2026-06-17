namespace Lite.Serialization.Protobuf.Attributes;

[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class GeneratedProtoSchemaAttribute(string fileName, string content) : Attribute
{
    public string FileName => fileName;
    public string Content => content;
}
