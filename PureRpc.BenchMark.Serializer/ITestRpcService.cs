using MemoryPack;
using MessagePack;
using ProtoBuf;
using System.Text.Json.Serialization;

namespace PureRpc.BenchMark.Serializer;

[MemoryPackable]
[MessagePackObject]
[ProtoContract]
public partial struct TestRequest
{
    [ProtoMember(1)]
    [Key(0)]
    public int A { get; set; }
    [ProtoMember(2)]
    [Key(1)]
    public int B { get; set; }
}
