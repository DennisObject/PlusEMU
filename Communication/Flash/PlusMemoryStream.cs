using Microsoft.IO;

namespace Plus.Communication.Flash;

public static class PlusMemoryStream
{
    public static readonly RecyclableMemoryStreamManager Manager = new();

    public static RecyclableMemoryStream GetStream() => (Manager.GetStream() as RecyclableMemoryStream)!;
    public static RecyclableMemoryStream GetStream(ReadOnlySpan<byte> buffer) => (Manager.GetStream(buffer) as RecyclableMemoryStream)!;
}
