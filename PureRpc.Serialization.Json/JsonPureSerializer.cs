using System.Buffers;
using System.Text.Json;
using PureRpc.Abstractions;

namespace PureRpc.Serialization.Json;

/// <summary>
/// 基于 System.Text.Json 的序列化器实现 / Serializer implementation based on System.Text.Json.
/// 使用 camelCase 命名策略进行 JSON 序列化 / Uses camelCase naming policy for JSON serialization.
/// </summary>
internal sealed class JsonPureSerializer : ISerializer
{
    /// <summary>
    /// JSON 序列化选项，使用 camelCase 命名策略 / 
    /// JSON serialization options using camelCase naming policy.
    /// </summary>
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// 将对象序列化到缓冲区写入器 / Serializes an object to the buffer writer.
    /// </summary>
    /// <typeparam name="T">对象类型 / The type of the object to serialize.</typeparam>
    /// <param name="writer">高性能缓冲区写入器 / High-performance buffer writer.</param>
    /// <param name="value">要序列化的值 / The value to serialize.</param>
    public void Serialize<T>(IBufferWriter<byte> writer, T value)
    {
        using var jsonWriter = new Utf8JsonWriter(writer);
        JsonSerializer.Serialize(jsonWriter, value, Options);
    }

    /// <summary>
    /// 从只读序列中反序列化对象 / Deserializes an object from a read-only byte sequence.
    /// </summary>
    /// <typeparam name="T">对象类型 / The type of the object to deserialize.</typeparam>
    /// <param name="sequence">只读字节序列 / Read-only byte sequence.</param>
    /// <returns>反序列化后的对象 / The deserialized object.</returns>
    public T Deserialize<T>(ReadOnlySequence<byte> sequence)
    {
        var reader = new Utf8JsonReader(sequence);
        return JsonSerializer.Deserialize<T>(ref reader, Options)!;
    }
}