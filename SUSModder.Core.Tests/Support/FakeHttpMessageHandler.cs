using System.Net;
using System.Text;

namespace SUSModder.Core.Tests.Support;

/// <summary>
/// Nagranie żądania HTTP wykonanego przez klienta API (bez ujawniania nagłówków autoryzacji).
/// </summary>
public sealed record RecordedRequest(
    HttpMethod Method,
    Uri Uri,
    string? Body,
    IReadOnlyDictionary<string, string> Headers)
{
    public string? GetHeader(string name) =>
        Headers.TryGetValue(name, out var value) ? value : null;

    public bool HasHeader(string name) => Headers.ContainsKey(name);
}

/// <summary>
/// Fake handler HTTP — pozwala testować transport Core bez ruchu sieciowego
/// i bez zgłoszeń do produkcyjnego API.
/// </summary>
public sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<RecordedRequest, HttpResponseMessage> _responder;

    public FakeHttpMessageHandler(Func<RecordedRequest, HttpResponseMessage> responder)
    {
        _responder = responder;
    }

    public List<RecordedRequest> Requests { get; } = new();

    public RecordedRequest LastRequest
    {
        get
        {
            lock (Requests)
            {
                Assert.NotEmpty(Requests);
                return Requests[^1];
            }
        }
    }

    public int RequestCount
    {
        get
        {
            lock (Requests)
            {
                return Requests.Count;
            }
        }
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        string? body = null;
        if (request.Content is not null)
            body = await request.Content.ReadAsStringAsync(cancellationToken);

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in request.Headers)
            headers[header.Key] = string.Join(", ", header.Value);

        if (request.Content is not null)
        {
            foreach (var header in request.Content.Headers)
                headers[header.Key] = string.Join(", ", header.Value);
        }

        var recorded = new RecordedRequest(
            request.Method,
            request.RequestUri ?? new Uri("https://api.test.invalid/"),
            body,
            headers);

        lock (Requests)
        {
            Requests.Add(recorded);
        }

        cancellationToken.ThrowIfCancellationRequested();

        return _responder(recorded);
    }

    public static HttpResponseMessage Json(
        HttpStatusCode statusCode,
        string json,
        IReadOnlyDictionary<string, string>? headers = null)
    {
        var response = new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        if (headers is not null)
        {
            foreach (var header in headers)
                response.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        return response;
    }
}
