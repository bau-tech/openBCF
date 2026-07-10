namespace OpenBcf.Core.Protocol;

/// <summary>
/// One entry from a buildingSMART server's /bcf/versions discovery response. Servers list every
/// API family they expose (e.g. "foundation", "bcf") in the same array, so callers must check
/// ApiId == "bcf" rather than assume the first entry is a BCF version.
/// </summary>
public sealed record BcfServerVersion(string? ApiId, string VersionId, string? DetailedVersion = null);
