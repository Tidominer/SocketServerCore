using System.Text.Json;

namespace SocketServerCore.Serialization;

public class JsonSerializer : ISerializer
{
    public string ContentType => "application/json";

    private readonly JsonSerializerOptions _options;

    public JsonSerializer(JsonSerializerOptions? options = null)
    {
        _options = options ?? new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    public byte[] Serialize<T>(T data)
    {
        return System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(data, _options);
    }

    public T? Deserialize<T>(ReadOnlySpan<byte> data)
    {
        return System.Text.Json.JsonSerializer.Deserialize<T>(data, _options);
    }

    public object? Deserialize(Type type, ReadOnlySpan<byte> data)
    {
        return System.Text.Json.JsonSerializer.Deserialize(data, type, _options);
    }
}