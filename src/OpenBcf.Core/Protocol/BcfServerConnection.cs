namespace OpenBcf.Core.Protocol;

public sealed record BcfServerConnection(
    Uri BaseUrl,
    string? AccessToken = null,
    string? BasicUsername = null,
    string? BasicPassword = null)
{
    public static readonly Uri DefaultServerUrl = new("https://bcf.chladny.de");

    public BcfServerConnection WithAccessToken(string accessToken) =>
        this with { AccessToken = accessToken, BasicUsername = null, BasicPassword = null };

    public BcfServerConnection WithBasicCredentials(string username, string password) =>
        this with { BasicUsername = username, BasicPassword = password, AccessToken = null };
}
