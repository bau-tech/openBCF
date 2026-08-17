using System.IO;
using System.Text;

namespace OpenBcf.ArchiCad29.Helper.Ipc;

/// <summary>
/// Packs/unpacks a list of IFC GUID strings to the layout the native
/// kCbGetSelectionGuids/kCbApplySelection handlers in
/// ../../OpenBcf.ArchiCad29.NativeAddOn/Src/HelperProcess.cpp use: int32 count, then per string an
/// int32 UTF-16 *character* count (not byte count - matches native's char16_t wcslen) followed by
/// that many UTF-16LE code units.
/// </summary>
internal static class GuidListWire
{
    public static byte[] Pack(IReadOnlyList<string> guids)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        writer.Write(guids.Count);
        foreach (var guid in guids)
        {
            writer.Write(guid.Length);
            writer.Write(Encoding.Unicode.GetBytes(guid));
        }

        return stream.ToArray();
    }

    public static List<string> Unpack(byte[] payload)
    {
        using var stream = new MemoryStream(payload);
        using var reader = new BinaryReader(stream);

        var count = reader.ReadInt32();
        var result = new List<string>(count);
        for (var i = 0; i < count; i++)
        {
            var charCount = reader.ReadInt32();
            var bytes = reader.ReadBytes(charCount * sizeof(char));
            result.Add(Encoding.Unicode.GetString(bytes));
        }

        return result;
    }
}
