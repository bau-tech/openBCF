using OpenBcf.Core.Protocol;

namespace OpenBcf.Tekla2026.Client;

/// <summary>
/// Holds the result of the most recent successful "Connect" - and a long-lived
/// <see cref="BcfServerClient"/> built from it - for the lifetime of the Tekla process (see
/// <see cref="BcfActiveSession"/>). Replaced wholesale by running Connect again; never persisted
/// as-is. Reusing one client (and its one <see cref="System.Net.Http.HttpClient"/>) across every
/// topic/comment/viewpoint call avoids opening a fresh connection per call.
/// </summary>
public static class BcfSession
{
    public static BcfActiveSession? Current { get; private set; }

    public static BcfServerClient? Client { get; private set; }

    public static void Set(BcfActiveSession session, BcfServerClient client)
    {
        Client?.Dispose();
        Current = session;
        Client = client;
    }

    public static void Clear()
    {
        Client?.Dispose();
        Current = null;
        Client = null;
    }
}
