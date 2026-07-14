using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using System.Text;
using System.Text.Json;
using TboxWebdav.Server.Modules.Tbox;
using TboxWebdav.Server.Modules.Tbox.Models;
using TboxWebdav.Server.Modules.Tbox.Services;
using TboxWebdav.Server.Modules.Webdav.Internal;
using Xunit;

namespace TboxWebdav.Server.Tests;

public class TboxMoveTests
{
    [Fact]
    public async Task DirectMoveItemAsync_File_UsesFileEndpoint()
    {
        var handler = CreateMoveHandler("file", HttpStatusCode.OK);
        var store = CreateStore(handler);

        var status = await store.DirectMoveItemAsync("source.txt", "folder/destination.txt");

        Assert.Equal(DavStatusCode.Ok, status);
        var request = Assert.Single(handler.Requests.Where(x => x.Method == HttpMethod.Put));
        Assert.Equal("/api/v1/file/library/space/folder/destination.txt", request.Uri.AbsolutePath);
        Assert.Equal("source.txt", GetJsonProperty(request.Content, "from"));
    }

    [Fact]
    public async Task DirectMoveItemAsync_Directory_UsesDirectoryEndpoint()
    {
        var handler = CreateMoveHandler("dir", HttpStatusCode.OK);
        var store = CreateStore(handler);

        var status = await store.DirectMoveItemAsync("source-directory", "parent/destination-directory");

        Assert.Equal(DavStatusCode.Ok, status);
        var request = Assert.Single(handler.Requests.Where(x => x.Method == HttpMethod.Put));
        Assert.Equal("/api/v1/directory/library/space/parent/destination-directory", request.Uri.AbsolutePath);
        Assert.Equal("source-directory", GetJsonProperty(request.Content, "from"));
    }

    [Fact]
    public void CopyOrMoveFile_Copy_UsesCopyFromAndKeepsFileEndpoint()
    {
        var handler = new RecordingHttpMessageHandler((_, _) => JsonResponse(HttpStatusCode.OK, "{\"path\":[\"copy.txt\"]}"));
        var service = CreateService(handler);

        var result = service.CopyOrMoveFile("source.txt", "copy.txt");

        Assert.True(result.Success);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("/api/v1/file/library/space/copy.txt", request.Uri.AbsolutePath);
        Assert.Equal("source.txt", GetJsonProperty(request.Content, "copyFrom"));
        Assert.False(HasJsonProperty(request.Content, "from"));
    }

    [Fact]
    public void CopyOrMoveDirectory_SpecialCharacters_AreEncodedSafely()
    {
        const string source = "中文 文件/C++ # % \"quote\" \\folder";
        const string destination = "目标 文件/C++ # % \"quote\" \\folder";
        var handler = new RecordingHttpMessageHandler((_, _) => JsonResponse(HttpStatusCode.OK, "{\"path\":[\"ok\"]}"));
        var service = CreateService(handler);

        var result = service.CopyOrMoveDirectory(source, destination, true);

        Assert.True(result.Success);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("application/json", request.ContentType);
        Assert.Equal(source, GetJsonProperty(request.Content, "from"));
        Assert.Contains("%E7%9B%AE%E6%A0%87%20%E6%96%87%E4%BB%B6", request.Uri.AbsoluteUri);
        Assert.Contains("C%2B%2B%20%23%20%25%20%22quote%22%20%5Cfolder", request.Uri.AbsoluteUri);
    }

    [Fact]
    public void CopyOrMoveDirectory_Error_PreservesStructuredResponse()
    {
        const string response = "{\"code\":\"DestinationAlreadyExists\",\"status\":409,\"message\":\"target exists\"}";
        var handler = new RecordingHttpMessageHandler((_, _) => JsonResponse(HttpStatusCode.Conflict, response));
        var service = CreateService(handler);

        var result = service.CopyOrMoveDirectory("source", "destination", true);

        Assert.False(result.Success);
        Assert.NotNull(result.Result);
        Assert.Equal(HttpStatusCode.Conflict, result.Result.HttpStatusCode);
        Assert.Equal("DestinationAlreadyExists", result.Result.Error?.Code);
        Assert.Equal("target exists", result.Result.Error?.Message);
        Assert.Equal(response, result.Result.ResponseContent);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, DavStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Forbidden, DavStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound, DavStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Conflict, DavStatusCode.Conflict)]
    [InlineData(HttpStatusCode.PreconditionFailed, DavStatusCode.PreconditionFailed)]
    [InlineData(HttpStatusCode.InternalServerError, DavStatusCode.InternalServerError)]
    public async Task DirectMoveItemAsync_BackendErrors_MapToWebDavStatus(HttpStatusCode backendStatus, DavStatusCode expectedStatus)
    {
        var handler = CreateMoveHandler("dir", backendStatus, "DirectoryMoveFailed");
        var store = CreateStore(handler);

        var status = await store.DirectMoveItemAsync("source", "destination");

        Assert.Equal(expectedStatus, status);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task DirectMoveItemAsync_MissingSource_ReturnsNotFoundWithoutMoveRequest()
    {
        var handler = new RecordingHttpMessageHandler((_, _) =>
            JsonResponse(HttpStatusCode.NotFound, "{\"code\":\"SourceFileNotFound\",\"status\":404,\"message\":\"missing\"}"));
        var store = CreateStore(handler);

        var status = await store.DirectMoveItemAsync("missing", "destination");

        Assert.Equal(DavStatusCode.NotFound, status);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
    }

    [Fact]
    public async Task DirectMoveItemAsync_UnknownSourceType_DoesNotCallMoveApi()
    {
        var handler = CreateMoveHandler("link", HttpStatusCode.OK);
        var store = CreateStore(handler);

        var status = await store.DirectMoveItemAsync("source", "destination");

        Assert.Equal(DavStatusCode.InternalServerError, status);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
    }

    [Fact]
    public async Task DirectMoveItemAsync_SourceLookupException_DoesNotCallMoveApi()
    {
        var handler = new RecordingHttpMessageHandler((_, _) => throw new HttpRequestException("lookup failed"));
        var store = CreateStore(handler);

        var status = await store.DirectMoveItemAsync("source", "destination");

        Assert.Equal(DavStatusCode.InternalServerError, status);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
    }

    private static TboxStore CreateStore(RecordingHttpMessageHandler handler)
    {
        return new TboxStore(NullLogger<TboxStore>.Instance, CreateService(handler), null!, null!);
    }

    private static TboxService CreateService(RecordingHttpMessageHandler handler)
    {
        var client = new HttpClient(handler);
        var cred = new TboxSpaceCred
        {
            AccessToken = "test-access-token",
            LibraryId = "library",
            SpaceId = "space",
            ExpiresIn = 3600
        };
        return new TboxService(client, "test-user-token", cred);
    }

    private static RecordingHttpMessageHandler CreateMoveHandler(string itemType, HttpStatusCode moveStatus, string errorCode = "MoveFailed")
    {
        return new RecordingHttpMessageHandler((_, requestNumber) =>
        {
            if (requestNumber == 1)
                return JsonResponse(HttpStatusCode.OK, $"{{\"type\":\"{itemType}\"}}");

            if ((int)moveStatus is >= 200 and <= 299)
                return JsonResponse(moveStatus, "{\"path\":[\"destination\"]}");

            return JsonResponse(moveStatus, $"{{\"code\":\"{errorCode}\",\"status\":{(int)moveStatus},\"message\":\"move failed\"}}");
        });
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string content)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(content, Encoding.UTF8, "application/json")
        };
    }

    private static string? GetJsonProperty(string? content, string propertyName)
    {
        using var document = JsonDocument.Parse(Assert.IsType<string>(content));
        return document.RootElement.GetProperty(propertyName).GetString();
    }

    private static bool HasJsonProperty(string? content, string propertyName)
    {
        using var document = JsonDocument.Parse(Assert.IsType<string>(content));
        return document.RootElement.TryGetProperty(propertyName, out _);
    }

    private sealed record CapturedRequest(HttpMethod Method, Uri Uri, string? Content, string? ContentType);

    private sealed class RecordingHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<CapturedRequest, int, HttpResponseMessage> _responder;

        public RecordingHttpMessageHandler(Func<CapturedRequest, int, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        public List<CapturedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var captured = new CapturedRequest(
                request.Method,
                request.RequestUri!,
                request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken),
                request.Content?.Headers.ContentType?.MediaType);
            Requests.Add(captured);
            return _responder(captured, Requests.Count);
        }
    }
}
