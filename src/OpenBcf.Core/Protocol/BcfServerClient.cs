using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using OpenBcf.Core.Abstractions;
using OpenBcf.Core.Model;
using OpenBcf.Core.Model.Visualization;
using OpenBcf.Core.Protocol.Dto;

namespace OpenBcf.Core.Protocol;

/// <summary>
/// Thrown by <see cref="BcfServerClient"/> instead of a plain <see cref="HttpRequestException"/>
/// so callers (see <see cref="BcfConnector"/>) can distinguish a 401 - which, for a token that the
/// local <see cref="BcfOAuthSessionCache"/> still believes is unexpired, means the server itself
/// invalidated/rotated it (restart, revoked session, clock drift) - from every other failure.
/// Derives from <see cref="HttpRequestException"/> so existing catch sites keep working unchanged.
/// </summary>
public sealed class BcfHttpRequestException : HttpRequestException
{
    // Not named "StatusCode" - HttpRequestException.StatusCode is nullable and .NET 5+ only, so
    // reusing that name would need a `new` that's a warning under net48 (no such base member
    // there) and a no-op under net8.0's target of this net48/net8.0-multi-targeted project.
    public HttpStatusCode ResponseStatusCode { get; }

    public BcfHttpRequestException(HttpStatusCode statusCode, string message) : base(message)
    {
        ResponseStatusCode = statusCode;
    }
}

/// <summary>
/// HTTP client for the buildingSMART BCF REST API. Works against any compliant server;
/// callers choose the server via <see cref="BcfServerConnection.BaseUrl"/>, defaulting to
/// <see cref="BcfServerConnection.DefaultServerUrl"/> (https://REDACTED-server.invalid) when none is supplied.
/// </summary>
public sealed class BcfServerClient : IBcfServerClient, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly BcfServerConnection _connection;
    private readonly bool _ownsHttpClient;

    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);

    public BcfServerClient(BcfServerConnection? connection = null)
        : this(
            connection ?? new BcfServerConnection(BcfServerConnection.DefaultServerUrl),
            // UseProxy=false avoids Windows' automatic proxy/WPAD auto-detection, which can hang
            // independently of HttpClient.Timeout and has been observed to freeze the host process.
            new HttpClient(new HttpClientHandler { UseProxy = false }) { Timeout = DefaultTimeout },
            ownsHttpClient: true)
    {
    }

    public BcfServerClient(BcfServerConnection connection, HttpClient httpClient)
        : this(connection, httpClient, ownsHttpClient: false)
    {
    }

    private BcfServerClient(BcfServerConnection connection, HttpClient httpClient, bool ownsHttpClient)
    {
        _connection = connection;
        _httpClient = httpClient;
        _httpClient.BaseAddress ??= connection.BaseUrl;
        _ownsHttpClient = ownsHttpClient;
    }

    public Uri BaseUrl => _connection.BaseUrl;

    public async Task<IReadOnlyList<BcfServerVersion>> GetServerVersionsAsync(CancellationToken cancellationToken = default)
    {
        var response = await SendAsync<ServerVersionsResponseDto>(HttpMethod.Get, "bcf/versions", null, cancellationToken).ConfigureAwait(false);
        return response.Versions.Select(v => new BcfServerVersion(v.ApiId, v.VersionId, v.DetailedVersion)).ToList();
    }

    public async Task<BcfServerAuthOptions> GetAuthOptionsAsync(string versionId, CancellationToken cancellationToken = default)
    {
        var dto = await SendAsync<AuthOptionsDto>(HttpMethod.Get, $"bcf/{versionId}/auth", null, cancellationToken).ConfigureAwait(false);
        return new BcfServerAuthOptions(
            dto.HttpBasicSupported,
            ParseUri(dto.OAuth2AuthorizationUrl),
            ParseUri(dto.OAuth2TokenUrl),
            ParseUri(dto.OAuth2DynamicClientRegistrationUrl),
            dto.SupportedOAuth2Flows);
    }

    public async Task<string> AuthenticateWithPasswordAsync(string versionId, string username, string password, CancellationToken cancellationToken = default)
    {
        var authOptions = await GetAuthOptionsAsync(versionId, cancellationToken).ConfigureAwait(false);
        var tokenUrl = authOptions.OAuth2TokenUrl
            ?? throw new InvalidOperationException("The server did not advertise an OAuth2 token endpoint.");

        using var request = new HttpRequestMessage(HttpMethod.Post, tokenUrl)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "password",
                ["username"] = username,
                ["password"] = password,
            }),
        };

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);

        var token = await response.Content.ReadFromJsonAsync<TokenResponseDto>(cancellationToken: cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("The token endpoint returned an empty response.");

        return token.AccessToken;
    }

    public async Task<IReadOnlyList<BcfProject>> GetProjectsAsync(string versionId, CancellationToken cancellationToken = default)
    {
        var dtos = await SendAsync<List<ProjectDto>>(HttpMethod.Get, $"bcf/{versionId}/projects", null, cancellationToken).ConfigureAwait(false);
        return dtos.Select(BcfRestMapper.ToDomain).ToList();
    }

    public async Task<BcfProject> GetProjectAsync(string versionId, string projectId, CancellationToken cancellationToken = default)
    {
        var dto = await SendAsync<ProjectDto>(HttpMethod.Get, $"bcf/{versionId}/projects/{Segment(projectId)}", null, cancellationToken).ConfigureAwait(false);
        return BcfRestMapper.ToDomain(dto);
    }

    public async Task<BcfProject> UpdateProjectAsync(string versionId, BcfProject project, CancellationToken cancellationToken = default)
    {
        var dto = await SendAsync<ProjectDto>(
            HttpMethod.Put,
            $"bcf/{versionId}/projects/{Segment(project.ProjectId)}",
            new ProjectWriteDto { Name = project.Name },
            cancellationToken).ConfigureAwait(false);
        return BcfRestMapper.ToDomain(dto);
    }

    public async Task<BcfProjectExtensions> GetProjectExtensionsAsync(string versionId, string projectId, CancellationToken cancellationToken = default)
    {
        var dto = await SendAsync<ExtensionDto>(HttpMethod.Get, $"bcf/{versionId}/projects/{Segment(projectId)}/extensions", null, cancellationToken).ConfigureAwait(false);
        return BcfRestMapper.ToDomain(dto);
    }

    public async Task<IReadOnlyList<BcfTopic>> GetTopicsAsync(string versionId, string projectId, CancellationToken cancellationToken = default)
    {
        var dtos = await SendAsync<List<TopicDto>>(HttpMethod.Get, $"bcf/{versionId}/projects/{Segment(projectId)}/topics", null, cancellationToken).ConfigureAwait(false);
        return dtos.Select(BcfRestMapper.ToDomain).ToList();
    }

    public async Task<BcfTopic> GetTopicAsync(string versionId, string projectId, Guid topicGuid, CancellationToken cancellationToken = default)
    {
        var dto = await SendAsync<TopicDto>(HttpMethod.Get, $"bcf/{versionId}/projects/{Segment(projectId)}/topics/{topicGuid}", null, cancellationToken).ConfigureAwait(false);
        return BcfRestMapper.ToDomain(dto);
    }

    public async Task<BcfTopic> CreateTopicAsync(string versionId, string projectId, BcfTopic topic, CancellationToken cancellationToken = default)
    {
        var dto = await SendAsync<TopicDto>(HttpMethod.Post, $"bcf/{versionId}/projects/{Segment(projectId)}/topics", BcfRestMapper.ToWriteDto(topic), cancellationToken).ConfigureAwait(false);
        return BcfRestMapper.ToDomain(dto);
    }

    public async Task<BcfTopic> UpdateTopicAsync(string versionId, string projectId, BcfTopic topic, CancellationToken cancellationToken = default)
    {
        var dto = await SendAsync<TopicDto>(HttpMethod.Put, $"bcf/{versionId}/projects/{Segment(projectId)}/topics/{topic.Guid}", BcfRestMapper.ToWriteDto(topic), cancellationToken).ConfigureAwait(false);
        return BcfRestMapper.ToDomain(dto);
    }

    public Task DeleteTopicAsync(string versionId, string projectId, Guid topicGuid, CancellationToken cancellationToken = default) =>
        SendNoContentAsync(HttpMethod.Delete, $"bcf/{versionId}/projects/{Segment(projectId)}/topics/{topicGuid}", cancellationToken);

    public async Task<BcfBimSnippet?> GetBimSnippetAsync(string versionId, string projectId, Guid topicGuid, CancellationToken cancellationToken = default)
    {
        var dto = await SendAsync<BimSnippetDto>(HttpMethod.Get, $"bcf/{versionId}/projects/{Segment(projectId)}/topics/{topicGuid}/snippet", null, cancellationToken).ConfigureAwait(false);
        return new BcfBimSnippet(dto.SnippetType, dto.Reference, dto.ReferenceSchema, dto.IsExternal ?? false);
    }

    public async Task PutBimSnippetAsync(string versionId, string projectId, Guid topicGuid, BcfBimSnippet snippet, byte[]? content = null, string? fileName = null, CancellationToken cancellationToken = default)
    {
        var url = $"bcf/{versionId}/projects/{Segment(projectId)}/topics/{topicGuid}/snippet";
        if (content is null)
        {
            await SendNoContentAsync(HttpMethod.Put, url, new BimSnippetDto
            {
                SnippetType = snippet.SnippetType,
                Reference = snippet.Reference,
                ReferenceSchema = snippet.ReferenceSchema,
                IsExternal = snippet.IsExternal,
            }, cancellationToken).ConfigureAwait(false);
            return;
        }

        using var request = CreateRequest(HttpMethod.Put, url);
        request.Content = new ByteArrayContent(content);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        if (!string.IsNullOrWhiteSpace(fileName))
            request.Content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment") { FileName = fileName };
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<BcfProjectFileInformation>> GetProjectFilesInformationAsync(string versionId, string projectId, CancellationToken cancellationToken = default)
    {
        var dtos = await SendAsync<List<ProjectFileInformationDto>>(HttpMethod.Get, $"bcf/{versionId}/projects/{Segment(projectId)}/files_information", null, cancellationToken).ConfigureAwait(false);
        return dtos.Select(BcfRestMapper.ToDomain).ToList();
    }

    public async Task<IReadOnlyList<BcfFileReference>> GetFilesAsync(string versionId, string projectId, Guid topicGuid, CancellationToken cancellationToken = default)
    {
        var dtos = await SendAsync<List<FileDto>>(HttpMethod.Get, $"bcf/{versionId}/projects/{Segment(projectId)}/topics/{topicGuid}/files", null, cancellationToken).ConfigureAwait(false);
        return dtos.Select(BcfRestMapper.ToDomain).ToList();
    }

    public async Task<IReadOnlyList<BcfFileReference>> PutFilesAsync(string versionId, string projectId, Guid topicGuid, IReadOnlyList<BcfFileReference> files, CancellationToken cancellationToken = default)
    {
        var dtos = await SendAsync<List<FileDto>>(
            HttpMethod.Put,
            $"bcf/{versionId}/projects/{Segment(projectId)}/topics/{topicGuid}/files",
            files.Select(BcfRestMapper.ToDto).ToList(),
            cancellationToken).ConfigureAwait(false);
        return dtos.Select(BcfRestMapper.ToDomain).ToList();
    }

    public async Task<IReadOnlyList<BcfComment>> GetCommentsAsync(string versionId, string projectId, Guid topicGuid, CancellationToken cancellationToken = default)
    {
        var dtos = await SendAsync<List<CommentDto>>(HttpMethod.Get, $"bcf/{versionId}/projects/{Segment(projectId)}/topics/{topicGuid}/comments", null, cancellationToken).ConfigureAwait(false);
        return dtos.Select(BcfRestMapper.ToDomain).ToList();
    }

    public async Task<BcfComment> GetCommentAsync(string versionId, string projectId, Guid topicGuid, Guid commentGuid, CancellationToken cancellationToken = default)
    {
        var dto = await SendAsync<CommentDto>(HttpMethod.Get, $"bcf/{versionId}/projects/{Segment(projectId)}/topics/{topicGuid}/comments/{commentGuid}", null, cancellationToken).ConfigureAwait(false);
        return BcfRestMapper.ToDomain(dto);
    }

    public async Task<BcfComment> CreateCommentAsync(string versionId, string projectId, Guid topicGuid, BcfComment comment, CancellationToken cancellationToken = default)
    {
        var dto = await SendAsync<CommentDto>(HttpMethod.Post, $"bcf/{versionId}/projects/{Segment(projectId)}/topics/{topicGuid}/comments", BcfRestMapper.ToWriteDto(comment), cancellationToken).ConfigureAwait(false);
        return BcfRestMapper.ToDomain(dto);
    }

    public async Task<BcfComment> UpdateCommentAsync(string versionId, string projectId, Guid topicGuid, BcfComment comment, CancellationToken cancellationToken = default)
    {
        var dto = await SendAsync<CommentDto>(HttpMethod.Put, $"bcf/{versionId}/projects/{Segment(projectId)}/topics/{topicGuid}/comments/{comment.Guid}", BcfRestMapper.ToWriteDto(comment), cancellationToken).ConfigureAwait(false);
        return BcfRestMapper.ToDomain(dto);
    }

    public Task DeleteCommentAsync(string versionId, string projectId, Guid topicGuid, Guid commentGuid, CancellationToken cancellationToken = default) =>
        SendNoContentAsync(HttpMethod.Delete, $"bcf/{versionId}/projects/{Segment(projectId)}/topics/{topicGuid}/comments/{commentGuid}", cancellationToken);

    public async Task<IReadOnlyList<BcfVisualizationInfo>> GetViewpointsAsync(string versionId, string projectId, Guid topicGuid, CancellationToken cancellationToken = default)
    {
        var dtos = await SendAsync<List<ViewpointDto>>(HttpMethod.Get, $"bcf/{versionId}/projects/{Segment(projectId)}/topics/{topicGuid}/viewpoints", null, cancellationToken).ConfigureAwait(false);
        return dtos.Select(BcfRestMapper.ToDomain).ToList();
    }

    public async Task<BcfVisualizationInfo> GetViewpointAsync(string versionId, string projectId, Guid topicGuid, Guid viewpointGuid, CancellationToken cancellationToken = default)
    {
        var dto = await SendAsync<ViewpointDto>(HttpMethod.Get, $"bcf/{versionId}/projects/{Segment(projectId)}/topics/{topicGuid}/viewpoints/{viewpointGuid}", null, cancellationToken).ConfigureAwait(false);
        return BcfRestMapper.ToDomain(dto);
    }

    public async Task<BcfVisualizationInfo> CreateViewpointAsync(string versionId, string projectId, Guid topicGuid, BcfVisualizationInfo viewpoint, byte[]? snapshotPngBytes = null, CancellationToken cancellationToken = default)
    {
        var dto = await SendAsync<ViewpointDto>(HttpMethod.Post, $"bcf/{versionId}/projects/{Segment(projectId)}/topics/{topicGuid}/viewpoints", BcfRestMapper.ToWriteDto(viewpoint, snapshotPngBytes), cancellationToken).ConfigureAwait(false);
        return BcfRestMapper.ToDomain(dto);
    }

    public async Task<byte[]> GetSnapshotAsync(string versionId, string projectId, Guid topicGuid, Guid viewpointGuid, CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Get, $"bcf/{versionId}/projects/{Segment(projectId)}/topics/{topicGuid}/viewpoints/{viewpointGuid}/snapshot");
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        return await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
    }

    public async Task<byte[]> GetBitmapAsync(string versionId, string projectId, Guid topicGuid, Guid viewpointGuid, string bitmapReference, CancellationToken cancellationToken = default) =>
        await GetBytesAsync($"bcf/{versionId}/projects/{Segment(projectId)}/topics/{topicGuid}/viewpoints/{viewpointGuid}/bitmaps/{Segment(bitmapReference)}", cancellationToken).ConfigureAwait(false);

    public Task DeleteViewpointAsync(string versionId, string projectId, Guid topicGuid, Guid viewpointGuid, CancellationToken cancellationToken = default) =>
        SendNoContentAsync(HttpMethod.Delete, $"bcf/{versionId}/projects/{Segment(projectId)}/topics/{topicGuid}/viewpoints/{viewpointGuid}", cancellationToken);

    public async Task<IReadOnlyList<Guid>> GetRelatedTopicsAsync(string versionId, string projectId, Guid topicGuid, CancellationToken cancellationToken = default)
    {
        var dtos = await SendAsync<List<RelatedTopicDto>>(HttpMethod.Get, $"bcf/{versionId}/projects/{Segment(projectId)}/topics/{topicGuid}/related_topics", null, cancellationToken).ConfigureAwait(false);
        return dtos.Select(d => Guid.Parse(d.RelatedTopicGuid)).ToList();
    }

    public async Task<IReadOnlyList<Guid>> PutRelatedTopicsAsync(string versionId, string projectId, Guid topicGuid, IReadOnlyList<Guid> relatedTopicGuids, CancellationToken cancellationToken = default)
    {
        var body = relatedTopicGuids.Select(g => new RelatedTopicDto { RelatedTopicGuid = g.ToString() }).ToList();
        var dtos = await SendAsync<List<RelatedTopicDto>>(HttpMethod.Put, $"bcf/{versionId}/projects/{Segment(projectId)}/topics/{topicGuid}/related_topics", body, cancellationToken).ConfigureAwait(false);
        return dtos.Select(d => Guid.Parse(d.RelatedTopicGuid)).ToList();
    }

    public async Task<IReadOnlyList<BcfDocumentReference>> GetDocumentReferencesAsync(string versionId, string projectId, Guid topicGuid, CancellationToken cancellationToken = default)
    {
        var dtos = await SendAsync<List<DocumentReferenceDto>>(HttpMethod.Get, $"bcf/{versionId}/projects/{Segment(projectId)}/topics/{topicGuid}/document_references", null, cancellationToken).ConfigureAwait(false);
        return dtos.Select(BcfRestMapper.ToDomain).ToList();
    }

    public async Task<BcfDocumentReference> CreateDocumentReferenceAsync(string versionId, string projectId, Guid topicGuid, BcfDocumentReference reference, CancellationToken cancellationToken = default)
    {
        var dto = await SendAsync<DocumentReferenceDto>(HttpMethod.Post, $"bcf/{versionId}/projects/{Segment(projectId)}/topics/{topicGuid}/document_references", BcfRestMapper.ToDto(reference), cancellationToken).ConfigureAwait(false);
        return BcfRestMapper.ToDomain(dto);
    }

    public async Task<BcfDocumentReference> UpdateDocumentReferenceAsync(string versionId, string projectId, Guid topicGuid, BcfDocumentReference reference, CancellationToken cancellationToken = default)
    {
        var dto = await SendAsync<DocumentReferenceDto>(HttpMethod.Put, $"bcf/{versionId}/projects/{Segment(projectId)}/topics/{topicGuid}/document_references/{reference.Guid}", BcfRestMapper.ToDto(reference), cancellationToken).ConfigureAwait(false);
        return BcfRestMapper.ToDomain(dto);
    }

    public async Task<IReadOnlyList<BcfServerDocument>> GetDocumentsAsync(string versionId, string projectId, CancellationToken cancellationToken = default)
    {
        var dtos = await SendAsync<List<DocumentDto>>(HttpMethod.Get, $"bcf/{versionId}/projects/{Segment(projectId)}/documents", null, cancellationToken).ConfigureAwait(false);
        return dtos.Select(BcfRestMapper.ToDomain).ToList();
    }

    public async Task<BcfServerDocument> CreateDocumentAsync(string versionId, string projectId, string fileName, byte[] content, CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Post, $"bcf/{versionId}/projects/{Segment(projectId)}/documents");
        request.Content = new ByteArrayContent(content);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        request.Content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment") { FileName = fileName };
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        var dto = await response.Content.ReadFromJsonAsync<DocumentDto>(cancellationToken: cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("Document upload returned an empty response.");
        return BcfRestMapper.ToDomain(dto);
    }

    public async Task<byte[]> GetDocumentAsync(string versionId, string projectId, Guid documentGuid, CancellationToken cancellationToken = default) =>
        await GetBytesAsync($"bcf/{versionId}/projects/{Segment(projectId)}/documents/{documentGuid}", cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<BcfEvent>> GetTopicEventsAsync(string versionId, string projectId, CancellationToken cancellationToken = default)
    {
        var dtos = await SendAsync<List<EventDto>>(HttpMethod.Get, $"bcf/{versionId}/projects/{Segment(projectId)}/topics/events", null, cancellationToken).ConfigureAwait(false);
        return dtos.Select(BcfRestMapper.ToDomain).ToList();
    }

    public async Task<IReadOnlyList<BcfEvent>> GetTopicEventsAsync(string versionId, string projectId, Guid topicGuid, CancellationToken cancellationToken = default)
    {
        var dtos = await SendAsync<List<EventDto>>(HttpMethod.Get, $"bcf/{versionId}/projects/{Segment(projectId)}/topics/{topicGuid}/events", null, cancellationToken).ConfigureAwait(false);
        return dtos.Select(BcfRestMapper.ToDomain).ToList();
    }

    public async Task<IReadOnlyList<BcfEvent>> GetCommentEventsAsync(string versionId, string projectId, Guid topicGuid, CancellationToken cancellationToken = default)
    {
        var dtos = await SendAsync<List<EventDto>>(HttpMethod.Get, $"bcf/{versionId}/projects/{Segment(projectId)}/topics/{topicGuid}/comments/events", null, cancellationToken).ConfigureAwait(false);
        return dtos.Select(BcfRestMapper.ToDomain).ToList();
    }

    public async Task<IReadOnlyList<BcfEvent>> GetCommentEventsAsync(string versionId, string projectId, Guid topicGuid, Guid commentGuid, CancellationToken cancellationToken = default)
    {
        var dtos = await SendAsync<List<EventDto>>(HttpMethod.Get, $"bcf/{versionId}/projects/{Segment(projectId)}/topics/{topicGuid}/comments/{commentGuid}/events", null, cancellationToken).ConfigureAwait(false);
        return dtos.Select(BcfRestMapper.ToDomain).ToList();
    }

    private async Task<TResponse> SendAsync<TResponse>(HttpMethod method, string relativeUrl, object? body, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(method, relativeUrl);
        if (body is not null)
            request.Content = JsonContent.Create(body);

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);

        return await response.Content.ReadFromJsonAsync<TResponse>(cancellationToken: cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException($"{relativeUrl} returned an empty response.");
    }

    private async Task SendNoContentAsync(HttpMethod method, string relativeUrl, CancellationToken cancellationToken) =>
        await SendNoContentAsync(method, relativeUrl, null, cancellationToken).ConfigureAwait(false);

    private async Task SendNoContentAsync(HttpMethod method, string relativeUrl, object? body, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(method, relativeUrl);
        if (body is not null)
            request.Content = JsonContent.Create(body);

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
    }

    private async Task<byte[]> GetBytesAsync(string relativeUrl, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Get, relativeUrl);
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        return await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string relativeUrl)
    {
        var request = new HttpRequestMessage(method, relativeUrl);
        if (_connection.AccessToken is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _connection.AccessToken);
        else if (_connection.BasicUsername is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", BasicAuthValue(_connection.BasicUsername, _connection.BasicPassword ?? string.Empty));

        return request;
    }

    /// <summary>
    /// <see cref="HttpResponseMessage.EnsureSuccessStatusCode"/> throws before anyone reads the
    /// response body, discarding the server's actual validation error (BCF servers return a JSON
    /// body explaining exactly which field failed, e.g. on a 422). Read it into the exception
    /// message instead of just reporting the status code.
    /// </summary>
    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
            return;

        // net48 doesn't have ReadAsStringAsync(CancellationToken) or the 3-arg
        // HttpRequestException(string, Exception, HttpStatusCode) ctor (both .NET 5+ only), and
        // OpenBcf.Core targets net48 alongside net8.0.
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        var detail = string.IsNullOrWhiteSpace(body) ? string.Empty : $": {body}";
        throw new BcfHttpRequestException(
            response.StatusCode,
            $"{(int)response.StatusCode} {response.ReasonPhrase} from {response.RequestMessage?.Method} {response.RequestMessage?.RequestUri}{detail}");
    }

    private static string BasicAuthValue(string username, string password) =>
        Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{username}:{password}"));

    private static Uri? ParseUri(string? value) => value is null ? null : new Uri(value);

    private static string Segment(string value) => Uri.EscapeDataString(value);

    public void Dispose()
    {
        if (_ownsHttpClient)
            _httpClient.Dispose();
    }
}
