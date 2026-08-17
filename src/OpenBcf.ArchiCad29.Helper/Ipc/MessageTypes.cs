namespace OpenBcf.ArchiCad29.Helper.Ipc;

/// <summary>
/// Wire message type bytes - must stay byte-for-byte identical to the constants in
/// ../../OpenBcf.ArchiCad29.NativeAddOn/Src/HelperProcess.cpp's anonymous namespace. See
/// PipeFraming for the frame format these are used inside.
/// </summary>
internal static class MessageTypes
{
    // Bridge pipe (this process is the server; native connects as client per JS::Function call).
    public const byte BridgeCall = 0x01;   // payload: int32 bindingNameCharCount, bindingName
                                            // (UTF-16LE), then payloadJson (UTF-16LE, rest of frame)
    public const byte BridgeResult = 0x82; // payload: resultJson (UTF-16LE) - the JSON envelope
                                            // {"isError":...} verbatim; native never interprets it.

    // Callbacks pipe (native is the server; this process connects as client per call).
    public const byte CbGetCamera = 0x10;
    public const byte CbGetSelectionGuids = 0x11;
    public const byte CbCaptureSnapshotPng = 0x12;
    public const byte CbApplyCamera = 0x13;
    public const byte CbApplySelection = 0x14;
    public const byte CbGetActiveProjectName = 0x15;
    public const byte CbExecuteJs = 0x16; // payload: script (UTF-16LE)

    // Shared response types.
    public const byte RespAck = 0x80;
    public const byte RespNack = 0x81;
    public const byte RespCameraData = 0x90;
    public const byte RespNoCamera = 0x91;
    public const byte RespGuidList = 0x92;
    public const byte RespSnapshotData = 0x93;
    public const byte RespNoSnapshot = 0x94;
    public const byte RespProjectName = 0x95;
}
