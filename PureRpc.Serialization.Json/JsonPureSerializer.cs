using System.Buffers;
using System.Text.Json;
using PureRpc.Abstractions;

namespace PureRpc.Serialization.Json;

internal sealed class JsonPureSerializer : ISerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public void Serialize<T>(IBufferWriter<byte> writer, T value)
    {
        using var jsonWriter = new Utf8JsonWriter(writer);
        JsonSerializer.Serialize(jsonWriter, value, Options);
    }

    public T Deserialize<T>(ReadOnlySequence<byte> sequence)
    {
        var reader = new Utf8JsonReader(sequence);
        return JsonSerializer.Deserialize<T>(ref reader, Options)!;
    }
}
