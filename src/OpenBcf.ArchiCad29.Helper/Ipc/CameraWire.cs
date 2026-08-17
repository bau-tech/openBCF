using OpenBcf.Core.Model;
using OpenBcf.Core.Model.Visualization;

namespace OpenBcf.ArchiCad29.Helper.Ipc;

/// <summary>
/// Packs/unpacks a <see cref="BcfCamera"/> to the fixed 92-byte layout PackCamera/UnpackCamera in
/// ../../OpenBcf.ArchiCad29.NativeAddOn/Src/HelperProcess.cpp use: int32 kind, then 9 doubles
/// (ViewPoint/Direction/UpVector), then FieldOfViewDegrees, then ViewToWorldScale - matching native
/// BcfCameraData's field order exactly (Src/Interop.h).
/// </summary>
internal static class CameraWire
{
    public const int Size = 4 + 9 * 8 + 8 + 8;

    public static byte[] Pack(BcfCamera camera)
    {
        var buffer = new byte[Size];
        var span = buffer.AsSpan();

        BitConverter.TryWriteBytes(span[..4], camera.Type == BcfCameraType.Perspective ? 0 : 1);
        WriteDouble(span, 4, camera.ViewPoint.X);
        WriteDouble(span, 12, camera.ViewPoint.Y);
        WriteDouble(span, 20, camera.ViewPoint.Z);
        WriteDouble(span, 28, camera.Direction.X);
        WriteDouble(span, 36, camera.Direction.Y);
        WriteDouble(span, 44, camera.Direction.Z);
        WriteDouble(span, 52, camera.UpVector.X);
        WriteDouble(span, 60, camera.UpVector.Y);
        WriteDouble(span, 68, camera.UpVector.Z);
        WriteDouble(span, 76, camera.FieldOfView ?? 0);
        WriteDouble(span, 84, camera.ViewToWorldScale ?? 0);

        return buffer;
    }

    public static BcfCamera Unpack(byte[] buffer)
    {
        var span = buffer.AsSpan();
        var kind = BitConverter.ToInt32(span[..4]);

        var viewPoint = new Point3D(ReadDouble(span, 4), ReadDouble(span, 12), ReadDouble(span, 20));
        var direction = new Point3D(ReadDouble(span, 28), ReadDouble(span, 36), ReadDouble(span, 44));
        var up = new Point3D(ReadDouble(span, 52), ReadDouble(span, 60), ReadDouble(span, 68));
        var fieldOfView = ReadDouble(span, 76);
        var viewToWorldScale = ReadDouble(span, 84);

        return kind == 0
            ? new BcfCamera(BcfCameraType.Perspective, viewPoint, direction, up, FieldOfView: fieldOfView)
            : new BcfCamera(BcfCameraType.Orthogonal, viewPoint, direction, up, ViewToWorldScale: viewToWorldScale);
    }

    private static void WriteDouble(Span<byte> span, int offset, double value) =>
        BitConverter.TryWriteBytes(span.Slice(offset, 8), value);

    private static double ReadDouble(ReadOnlySpan<byte> span, int offset) =>
        BitConverter.ToDouble(span.Slice(offset, 8));
}
