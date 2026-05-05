namespace Lite.Serialization.Protobuf.WireFormat;

public enum ProtoWireType : byte
{
    Varint = 0,
    Fixed64 = 1,
    LengthDelimited = 2,
    Fixed32 = 5,
}

public static class ProtoTag
{
    public static uint Make(int fieldNumber, ProtoWireType wireType) =>
        ((uint)fieldNumber << 3) | (uint)wireType;

    public static (int FieldNumber, ProtoWireType WireType) Split(uint tag) =>
        ((int)(tag >> 3), (ProtoWireType)(tag & 0b111));
}
