// Copyright © 2026 Krzysztof Kasprowicz

using System.Collections.ObjectModel;
using System.Net.Http.Headers;

namespace MailMcp.TestDoubles;

/// <summary>
/// An <see cref="HttpMessageHandler" /> that answers requests from a test-supplied script and records every request
/// it observed, so an HTTP adapter can be unit-tested without reaching a network.
/// </summary>
/// <remarks>
/// <para>
/// This is the suite's only HTTP test double. NSubstitute cannot produce one, because <see cref="SendAsync" /> is
/// protected and a substitute cannot override it, so the handler is hand-written and shared from <c>tests/shared/</c>
/// rather than duplicated per test project.
/// </para>
/// <para>
/// Only the asynchronous send path is implemented. A synchronous <see cref="HttpClient.Send(HttpRequestMessage)" />
/// call against this handler throws, which is intended: production code is asynchronous end-to-end, and a test that
/// hits the synchronous path is exercising a shape the repository does not allow.
/// </para>
/// <para>
/// Disposing the handler disposes every response of a script, including responses already handed out, so read a
/// response body before the handler goes out of scope. A handler owned by an <see cref="HttpClient" /> constructed
/// with <c>disposeHandler: true</c> is torn down with that client.
/// </para>
/// <para>
/// Recording is safe under concurrent sends, so a test covering bounded concurrency can assert against
/// <see cref="RecordedRequests" /> once its requests have completed. The recorded order is the order in which
/// requests reached the handler, which under concurrency is not the order they were started in.
/// </para>
/// </remarks>
internal sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> NoHeaders =
        ReadOnlyDictionary<string, IReadOnlyList<string>>.Empty;

    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respondToRequest;
    private readonly HttpResponseMessage[] scriptedResponses;
    private readonly List<RecordedHttpRequest> recordedRequests = [];
    private readonly Lock recordingGuard = new();

    private int scriptedResponseCursor;

    /// <summary>
    /// Initializes a handler that answers every request through <paramref name="respondToRequest" />.
    /// </summary>
    /// <param name="respondToRequest">
    /// Produces the response for a request. Use this overload when the response depends on the request, when the
    /// handler must throw to simulate a transport failure, or when it must observe the cancellation token. An
    /// exception it raises reaches the caller unwrapped, because <see cref="HttpClient" /> translates failures into
    /// <see cref="HttpRequestException" /> only for its own socket stack.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="respondToRequest" /> is <see langword="null" />.</exception>
    public FakeHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respondToRequest)
    {
        ArgumentNullException.ThrowIfNull(respondToRequest);

        this.respondToRequest = respondToRequest;
        this.scriptedResponses = [];
    }

    private FakeHttpMessageHandler(HttpResponseMessage[] scriptedResponses)
    {
        this.scriptedResponses = scriptedResponses;
        this.respondToRequest = (request, _) => Task.FromResult(this.NextScriptedResponse(request));
    }

    /// <summary>
    /// Gets the requests observed so far, oldest first, as an independent snapshot.
    /// </summary>
    public IReadOnlyList<RecordedHttpRequest> RecordedRequests
    {
        get
        {
            lock (this.recordingGuard)
            {
                return [.. this.recordedRequests];
            }
        }
    }

    /// <summary>
    /// Creates a handler that answers every request with a freshly built response.
    /// </summary>
    /// <param name="createResponse">
    /// Builds one response per request. It is a factory rather than a single instance because a response is consumed
    /// once: its content stream is read and the caller disposes it, so handing the same instance to a second request
    /// would surface as an empty or disposed body instead of as the configured response.
    /// </param>
    /// <returns>A handler that never runs out of responses.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="createResponse" /> is <see langword="null" />.</exception>
    public static FakeHttpMessageHandler AlwaysResponding(Func<HttpResponseMessage> createResponse)
    {
        ArgumentNullException.ThrowIfNull(createResponse);

        return new FakeHttpMessageHandler((_, _) => Task.FromResult(createResponse()));
    }

    /// <summary>
    /// Creates a handler that answers each request with the next response in order, which is how a retry, a redirect,
    /// or a paged fetch is scripted.
    /// </summary>
    /// <param name="scriptedResponses">The responses to return, one per request, in order.</param>
    /// <returns>A handler that throws once the script is exhausted.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="scriptedResponses" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException"><paramref name="scriptedResponses" /> is empty.</exception>
    public static FakeHttpMessageHandler RespondingInSequence(params HttpResponseMessage[] scriptedResponses)
    {
        ArgumentNullException.ThrowIfNull(scriptedResponses);

        if (scriptedResponses.Length == 0)
        {
            throw new ArgumentException(
                "A sequenced handler needs at least one response; use AlwaysResponding for an unbounded script.",
                nameof(scriptedResponses));
        }

        return new FakeHttpMessageHandler([.. scriptedResponses]);
    }

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var recordedRequest = await CaptureAsync(request, cancellationToken);

        lock (this.recordingGuard)
        {
            this.recordedRequests.Add(recordedRequest);
        }

        return await this.respondToRequest(request, cancellationToken);
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var scriptedResponse in this.scriptedResponses)
            {
                scriptedResponse.Dispose();
            }
        }

        base.Dispose(disposing);
    }

    private static async Task<RecordedHttpRequest> CaptureAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var content = request.Content is null
            ? ReadOnlyMemory<byte>.Empty
            : await request.Content.ReadAsByteArrayAsync(cancellationToken);

        return new RecordedHttpRequest(
            request.Method,
            request.RequestUri,
            CopyHeaders(request.Headers),
            request.Content is null ? NoHeaders : CopyHeaders(request.Content.Headers),
            content);
    }

    private static Dictionary<string, IReadOnlyList<string>> CopyHeaders(HttpHeaders headers) =>
        headers.ToDictionary(
            header => header.Key,
            header => (IReadOnlyList<string>)[.. header.Value],
            StringComparer.OrdinalIgnoreCase);

    private HttpResponseMessage NextScriptedResponse(HttpRequestMessage request)
    {
        var responseIndex = Interlocked.Increment(ref this.scriptedResponseCursor) - 1;

        if (responseIndex >= this.scriptedResponses.Length)
        {
            throw new InvalidOperationException(
                $"The handler was scripted with {this.scriptedResponses.Length} response(s) but received request "
                + $"{responseIndex + 1}: {request.Method} {request.RequestUri}.");
        }

        return this.scriptedResponses[responseIndex];
    }
}
