using OpenBcf.Core.Protocol;

namespace OpenBcf.ArchiCad29.Helper;

/// <summary>
/// Holds the result of the most recent successful "Connect" - and a long-lived
/// <see cref="BcfServerClient"/> built from it - for the lifetime of this helper process. Mirrors
/// every other client's BcfSession exactly.
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
