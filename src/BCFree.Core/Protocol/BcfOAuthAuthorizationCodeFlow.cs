using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using BCFree.Core.Protocol.Dto;

namespace BCFree.Core.Protocol;

/// <summary>
/// Implements OAuth2 Authorization Code grant (RFC 6749) with dynamic client registration
/// (RFC 7591). The BCF API spec has no "password" grant - a server that only advertises
/// authorization_code_grant (like bcf.chladny.de) can only be signed into by opening the
/// server's own login page in a browser, not by posting a username/password from the plugin.
/// This opens the user's default browser to that page and captures the redirect on a local
/// loopback listener, the standard pattern for native/desktop OAuth2 clients.
/// </summary>
public static class BcfOAuthAuthorizationCodeFlow
{
    private static readonly TimeSpan HttpTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan SignInTimeout = TimeSpan.FromMinutes(3);

    public static async Task<string> AuthenticateAsync(BcfServerAuthOptions options, Uri serverBaseUrl, CancellationToken cancellationToken = default)
    {
        if (!options.SupportsAuthorizationCodeGrant)
            throw new InvalidOperationException($"{serverBaseUrl} does not advertise an OAuth2 authorization_code grant.");

        var cached = BcfOAuthSessionCache.TryGet(serverBaseUrl);
        // A session with no access token (e.g. a previous run that was interrupted mid-exchange,
        // or any other partial/corrupted write) must never be trusted just because its expiry
        // looks fine - that sends "Authorization: Bearer " (empty) on every request, which the
        // server rejects with 401 forever since nothing here would ever notice and re-authenticate.
        if (cached is { AccessToken: { Length: > 0 } } && cached.ExpiresAtUtc > DateTimeOffset.UtcNow.AddSeconds(30))
            return cached.AccessToken;

        using var httpClient = new HttpClient(new HttpClientHandler { UseProxy = false }) { Timeout = HttpTimeout };

        if (cached is not null)
        {
            var registeredClient = new RegisteredClientInfo(cached.ClientId, cached.ClientSecret, cached.TokenEndpointAuthMethod);

            if (cached.RefreshToken is { Length: > 0 } refreshToken)
            {
                var refreshed = await TryRefreshAsync(httpClient, options.OAuth2TokenUrl!, registeredClient, cached.RedirectUri, refreshToken, cancellationToken);
                if (refreshed is not null)
                {
                    BcfOAuthSessionCache.Set(serverBaseUrl, refreshed);
                    return refreshed.AccessToken;
                }
            }

            // Refresh wasn't available or failed - fall back to a fresh interactive sign-in,
            // reusing the already-registered client and its redirect URI.
            var session = await RunInteractiveSignInAsync(httpClient, options, registeredClient, cached.RedirectUri, cancellationToken);
            BcfOAuthSessionCache.Set(serverBaseUrl, session);
            return session.AccessToken;
        }

        var redirectUri = $"http://127.0.0.1:{GetFreeLoopbackPort()}/callback/";
        var client = await RegisterClientAsync(httpClient, options.OAuth2DynamicClientRegistrationUrl, redirectUri, cancellationToken);

        var newSession = await RunInteractiveSignInAsync(httpClient, options, client, redirectUri, cancellationToken);
        BcfOAuthSessionCache.Set(serverBaseUrl, newSession);
        return newSession.AccessToken;
    }

    private static async Task<RegisteredClientInfo> RegisterClientAsync(HttpClient httpClient, Uri? registrationUrl, string redirectUri, CancellationToken cancellationToken)
    {
        if (registrationUrl is null)
            throw new InvalidOperationException("The server did not advertise a dynamic client registration endpoint.");

        var requestDto = new ClientRegistrationRequestDto { RedirectUris = { redirectUri } };

        using var response = await httpClient.PostAsJsonAsync(registrationUrl, requestDto, cancellationToken);
        response.EnsureSuccessStatusCode();

        var dto = await response.Content.ReadFromJsonAsync<ClientRegistrationResponseDto>(cancellationToken: cancellationToken)
            ?? throw new InvalidDataException("Dynamic client registration returned an empty response.");

        return new RegisteredClientInfo(dto.ClientId, dto.ClientSecret, dto.TokenEndpointAuthMethod ?? "client_secret_basic");
    }

    private static async Task<CachedOAuthSession> RunInteractiveSignInAsync(
        HttpClient httpClient, BcfServerAuthOptions options, RegisteredClientInfo client, string redirectUri, CancellationToken cancellationToken)
    {
        var state = Guid.NewGuid().ToString("N");
        var authorizeUri = BuildAuthorizeUri(options.OAuth2AuthorizationUrl!, client.ClientId, redirectUri, state);

        using var listener = new HttpListener();
        listener.Prefixes.Add(redirectUri);
        listener.Start();

        try
        {
            OpenSignInWindow(authorizeUri);

            var contextTask = listener.GetContextAsync();
            var timeoutTask = Task.Delay(SignInTimeout, cancellationToken);
            if (await Task.WhenAny(contextTask, timeoutTask) == timeoutTask)
                throw new TimeoutException("Timed out waiting for the browser sign-in to complete.");

            var context = await contextTask;
            var code = context.Request.QueryString["code"];
            var returnedState = context.Request.QueryString["state"];
            var error = context.Request.QueryString["error"];

            await RespondToBrowserAsync(context, success: error is null && code is not null);

            if (error is not null)
                throw new InvalidOperationException($"The server denied sign-in: {error}.");
            if (returnedState != state || string.IsNullOrEmpty(code))
                throw new InvalidOperationException("OAuth2 sign-in failed or was cancelled.");

            return await ExchangeCodeForTokenAsync(httpClient, options.OAuth2TokenUrl!, client, redirectUri, code!, cancellationToken);
        }
        finally
        {
            listener.Stop();
        }
    }

    private static async Task RespondToBrowserAsync(HttpListenerContext context, bool success)
    {
        var html = success
            ? "<html><body><h3>Signed in to openBCF.</h3><p>You can close this window and return to your application.</p></body></html>"
            : "<html><body><h3>Sign-in failed or was cancelled.</h3><p>You can close this window and return to your application.</p></body></html>";

        var buffer = Encoding.UTF8.GetBytes(html);
        context.Response.ContentType = "text/html";
        context.Response.ContentLength64 = buffer.Length;
        await context.Response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
        context.Response.OutputStream.Close();
    }

    private static async Task<CachedOAuthSession> ExchangeCodeForTokenAsync(
        HttpClient httpClient, Uri tokenUrl, RegisteredClientInfo client, string redirectUri, string code, CancellationToken cancellationToken)
    {
        using var request = BuildTokenRequest(tokenUrl, client, new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
        });

        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var dto = await response.Content.ReadFromJsonAsync<TokenResponseDto>(cancellationToken: cancellationToken)
            ?? throw new InvalidDataException("The token endpoint returned an empty response.");

        return ToSession(dto, client, redirectUri);
    }

    private static async Task<CachedOAuthSession?> TryRefreshAsync(
        HttpClient httpClient, Uri tokenUrl, RegisteredClientInfo client, string redirectUri, string refreshToken, CancellationToken cancellationToken)
    {
        using var request = BuildTokenRequest(tokenUrl, client, new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
        });

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return null;

        var dto = await response.Content.ReadFromJsonAsync<TokenResponseDto>(cancellationToken: cancellationToken);
        return dto is null ? null : ToSession(dto, client, redirectUri);
    }

    private static HttpRequestMessage BuildTokenRequest(Uri tokenUrl, RegisteredClientInfo client, Dictionary<string, string> formFields)
    {
        formFields["client_id"] = client.ClientId;
        var request = new HttpRequestMessage(HttpMethod.Post, tokenUrl);

        if (string.Equals(client.TokenEndpointAuthMethod, "client_secret_basic", StringComparison.OrdinalIgnoreCase) && client.ClientSecret is not null)
        {
            var raw = $"{client.ClientId}:{client.ClientSecret}";
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(raw)));
        }
        else if (client.ClientSecret is not null)
        {
            // client_secret_post (or any non-Basic method): the secret travels in the body.
            formFields["client_secret"] = client.ClientSecret;
        }

        request.Content = new FormUrlEncodedContent(formFields);
        return request;
    }

    private static CachedOAuthSession ToSession(TokenResponseDto dto, RegisteredClientInfo client, string redirectUri) =>
        new(
            dto.AccessToken,
            DateTimeOffset.UtcNow.AddSeconds(dto.ExpiresIn ?? 3600),
            dto.RefreshToken,
            client.ClientId,
            client.ClientSecret,
            client.TokenEndpointAuthMethod,
            redirectUri);

    private static Uri BuildAuthorizeUri(Uri authorizationUrl, string clientId, string redirectUri, string state)
    {
        var separator = authorizationUrl.Query.Length > 0 ? "&" : "?";
        return new Uri(
            $"{authorizationUrl}{separator}response_type=code&client_id={Uri.EscapeDataString(clientId)}" +
            $"&redirect_uri={Uri.EscapeDataString(redirectUri)}&state={Uri.EscapeDataString(state)}");
    }

    /// <summary>
    /// Opens the sign-in page as a small, fixed-size popup instead of a regular (often
    /// maximized) browser window. Chromium's "--app" mode is the standard trick desktop OAuth
    /// clients use for this; if no Chromium browser is found, falls back to the OS default
    /// handler, which opens at whatever size/state the user's browser normally starts in.
    /// </summary>
    private static void OpenSignInWindow(Uri url)
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        var candidates = new[]
        {
            Path.Combine(programFiles, "Microsoft", "Edge", "Application", "msedge.exe"),
            Path.Combine(programFilesX86, "Microsoft", "Edge", "Application", "msedge.exe"),
            Path.Combine(programFiles, "Google", "Chrome", "Application", "chrome.exe"),
            Path.Combine(programFilesX86, "Google", "Chrome", "Application", "chrome.exe"),
        };

        foreach (var browserExe in candidates)
        {
            if (!File.Exists(browserExe))
                continue;

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = browserExe,
                    Arguments = $"--app={url} --window-size=480,720",
                    UseShellExecute = true,
                });
                return;
            }
            catch
            {
                // Fall through and try the next candidate, or the OS default below.
            }
        }

        Process.Start(new ProcessStartInfo(url.ToString()) { UseShellExecute = true });
    }

    private static int GetFreeLoopbackPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private sealed record RegisteredClientInfo(string ClientId, string? ClientSecret, string TokenEndpointAuthMethod);
}
