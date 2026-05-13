namespace SocketServerCore.Serialization;

public interface ISerializer
{
    string ContentType { get; }
    byte[] Serialize<T>(T data);
    T? Deserialize<T>(ReadOnlySpan<byte> data);
    object? Deserialize(Type type, ReadOnlySpan<byte> data);
}