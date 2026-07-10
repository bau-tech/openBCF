using OpenBcf.Core.Model;

namespace OpenBcf.Core.Protocol;

/// <summary>
/// The result of a successful "Connect" action: an authenticated connection, the BCF API version
/// it was negotiated against, and the project this host model is locked to. Each host plugin
/// holds the current one in memory for the lifetime of the host process (see e.g.
/// <c>OpenBcf.Revit2025.Client.BcfSession</c>) - running Connect again replaces it, closing the
/// host drops it. Nothing here is persisted as-is; only the project mapping survives via
/// <see cref="Configuration.BcfProjectMappingStore"/> and the OAuth session via
/// <see cref="BcfOAuthSessionCache"/>.
/// </summary>
public sealed record BcfActiveSession(
    BcfServerConnection Connection,
    string VersionId,
    BcfProject Project,
    string ModelKey);
