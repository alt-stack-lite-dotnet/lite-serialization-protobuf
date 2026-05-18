using System;
using System.Buffers;

namespace Lite.Serialization.Protobuf;

/// <summary>
/// A pooled buffer holding a serialized message. The underlying memory is rented from
/// <see cref="MemoryPool{T}.Shared"/>; the slice exposed via <see cref="Memory"/> is
/// trimmed to the actual written length. Caller must <see cref="Dispose"/> when done.
/// </summary>
public readonly struct RentedBuffer : IDisposable
{
    private readonly IMemoryOwner<byte>? _owner;
    private readonly int _length;

    public RentedBuffer(IMemoryOwner<byte> owner, int length)
    {
        _owner = owner;
        _length = length;
    }

    public ReadOnlyMemory<byte> Memory =>
        _owner is null ? ReadOnlyMemory<byte>.Empty : _owner.Memory.Slice(0, _length);

    public ReadOnlySpan<byte> Span =>
        _owner is null ? ReadOnlySpan<byte>.Empty : _owner.Memory.Span.Slice(0, _length);

    public int Length => _length;

    public void Dispose() => _owner?.Dispose();
}
