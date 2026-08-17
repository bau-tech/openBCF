using System.IO;

namespace OpenBcf.ArchiCad29.Helper.Ipc;

/// <summary>
/// Reads/writes the shared frame format both pipes use: a 4-byte little-endian payload length,
/// a 1-byte message type (see <see cref="MessageTypes"/>), then that many payload bytes. Must stay
/// exactly in sync with WriteFrame/ReadFrame in
/// ../../OpenBcf.ArchiCad29.NativeAddOn/Src/HelperProcess.cpp.
/// </summary>
internal static class PipeFraming
{
    public static void WriteFrame(Stream stream, byte messageType, ReadOnlySpan<byte> payload)
    {
        Span<byte> header = stackalloc byte[5];
        BitConverter.TryWriteBytes(header[..4], payload.Length);
        header[4] = messageType;
        stream.Write(header);
        if (payload.Length > 0)
            stream.Write(payload);
        stream.Flush();
    }

    public static (byte MessageType, byte[] Payload) ReadFrame(Stream stream)
    {
        var header = ReadExact(stream, 5);
        var payloadLength = BitConverter.ToInt32(header, 0);
        var messageType = header[4];
        var payload = payloadLength > 0 ? ReadExact(stream, payloadLength) : Array.Empty<byte>();
        return (messageType, payload);
    }

    private static byte[] ReadExact(Stream stream, int count)
    {
        var buffer = new byte[count];
        var offset = 0;
        while (offset < count)
        {
            var read = stream.Read(buffer, offset, count - offset);
            if (read == 0)
                throw new IOException("Pipe closed while reading a frame.");
            offset += read;
        }
        return buffer;
    }
}
