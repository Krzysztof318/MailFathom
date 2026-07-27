// Copyright © 2026 Krzysztof Kasprowicz

using System.Globalization;
using System.Net;
using System.Text;
using MailMcp.TestSupport;
using Xunit;

namespace MailMcp.SharedSources.UnitTests;

/// <summary>
/// Proves the shared HTTP test double, because a fault in it reports a false result in every adapter suite that
/// relies on it rather than failing where the fault is.
/// </summary>
public sealed class FakeHttpMessageHandlerTests
{
    private static readonly Uri RequestUri = new("https://mail.test/messages");

    [Fact]
    public async Task RecordedRequests_RequestWithHeaderAndJsonBody_CapturesMethodUriHeaderAndBody()
    {
        // Arrange
        using var handler = FakeHttpMessageHandler.AlwaysResponding(() => new HttpResponseMessage(HttpStatusCode.OK));
        using var client = new HttpClient(handler, disposeHandler: false);
        using var request = new HttpRequestMessage(HttpMethod.Post, RequestUri)
        {
            Content = new StringContent("""{"folder":"INBOX"}""", Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-Correlation-Id", "abc-123");

        // Act
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        // Assert
        var recordedRequest = Assert.Single(handler.RecordedRequests);
        Assert.Equal(HttpMethod.Post, recordedRequest.Method);
        Assert.Equal(RequestUri, recordedRequest.RequestUri);
        Assert.Equal(["abc-123"], recordedRequest.Headers["x-correlation-id"]);
        Assert.Equal("""{"folder":"INBOX"}""", recordedRequest.ContentAsUtf8String());
        Assert.Equal(["application/json; charset=utf-8"], recordedRequest.ContentHeaders["Content-Type"]);
    }

    /// <summary>
    /// The recorded state is the reason this handler exists in its current shape: <see cref="HttpClient" /> owns the
    /// request message and tears it down once the response completes, so a double that kept the message itself would
    /// hand assertions state that is already disposed.
    /// </summary>
    [Fact]
    public async Task RecordedRequests_RequestMessageDisposedAfterSending_RemainReadable()
    {
        // Arrange
        using var handler = FakeHttpMessageHandler.AlwaysResponding(() => new HttpResponseMessage(HttpStatusCode.OK));
        using var client = new HttpClient(handler, disposeHandler: false);
        var request = new HttpRequestMessage(HttpMethod.Put, RequestUri)
        {
            Content = new StringContent("body", Encoding.UTF8, "text/plain"),
        };
        request.Headers.Add("X-Correlation-Id", "abc-123");

        // Act
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        request.Dispose();

        // Assert
        var recordedRequest = Assert.Single(handler.RecordedRequests);
        Assert.Equal(["abc-123"], recordedRequest.Headers["X-Correlation-Id"]);
        Assert.Equal("body", recordedRequest.ContentAsUtf8String());
    }

    [Fact]
    public async Task RecordedRequests_RequestWithoutBody_CapturesEmptyContentAndNoContentHeaders()
    {
        // Arrange
        using var handler = FakeHttpMessageHandler.AlwaysResponding(() => new HttpResponseMessage(HttpStatusCode.OK));
        using var client = new HttpClient(handler, disposeHandler: false);

        // Act
        using var response = await client.GetAsync(RequestUri, TestContext.Current.CancellationToken);

        // Assert
        var recordedRequest = Assert.Single(handler.RecordedRequests);
        Assert.True(recordedRequest.Content.IsEmpty);
        Assert.Empty(recordedRequest.ContentHeaders);
        Assert.Equal(string.Empty, recordedRequest.ContentAsUtf8String());
    }

    [Fact]
    public async Task SendAsync_ScriptedSequence_AnswersRequestsInOrder()
    {
        // Arrange
        using var handler = FakeHttpMessageHandler.RespondingInSequence(
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
            new HttpResponseMessage(HttpStatusCode.OK));
        using var client = new HttpClient(handler, disposeHandler: false);

        // Act
        var statusCodes = new List<HttpStatusCode>();
        for (var attempt = 0; attempt < 3; attempt++)
        {
            using var response = await client.GetAsync(RequestUri, TestContext.Current.CancellationToken);
            statusCodes.Add(response.StatusCode);
        }

        // Assert
        Assert.Equal(
            [HttpStatusCode.ServiceUnavailable, HttpStatusCode.ServiceUnavailable, HttpStatusCode.OK],
            statusCodes);
    }

    /// <summary>
    /// An exhausted script means the code under test sent more requests than the test expected, which is a finding in
    /// its own right. It must name the request that ran past the end instead of surfacing an index error.
    /// </summary>
    [Fact]
    public async Task SendAsync_ScriptExhausted_FailsNamingTheRequestNumberAndTarget()
    {
        // Arrange
        using var handler = FakeHttpMessageHandler.RespondingInSequence(new HttpResponseMessage(HttpStatusCode.OK));
        using var client = new HttpClient(handler, disposeHandler: false);
        using var firstResponse = await client.GetAsync(RequestUri, TestContext.Current.CancellationToken);

        // Act
        var scriptExhaustion = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.GetAsync(RequestUri, TestContext.Current.CancellationToken));

        // Assert
        Assert.Contains("scripted with 1 response(s)", scriptExhaustion.Message, StringComparison.Ordinal);
        Assert.Contains("received request 2", scriptExhaustion.Message, StringComparison.Ordinal);
        Assert.Contains(RequestUri.AbsoluteUri, scriptExhaustion.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A response is consumed once, so the repeated form takes a factory. Reading the body of every response proves
    /// the handler does not hand the same already-read instance back a second time.
    /// </summary>
    [Fact]
    public async Task AlwaysResponding_SeveralRequests_AnswersEachWithAFreshlyBuiltResponse()
    {
        // Arrange
        using var handler = FakeHttpMessageHandler.AlwaysResponding(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"total":0}""", Encoding.UTF8, "application/json"),
        });
        using var client = new HttpClient(handler, disposeHandler: false);

        // Act
        var bodies = new List<string>();
        for (var attempt = 0; attempt < 3; attempt++)
        {
            using var response = await client.GetAsync(RequestUri, TestContext.Current.CancellationToken);
            bodies.Add(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        }

        // Assert
        Assert.Equal(["""{"total":0}""", """{"total":0}""", """{"total":0}"""], bodies);
    }

    /// <summary>
    /// A responder that throws models a transport failure. <see cref="HttpClient" /> wraps such a failure in an
    /// <see cref="HttpRequestException" /> only for its own socket stack, so a failure raised here reaches the caller
    /// as the exception the test threw, which is what a retry-policy test asserts against.
    /// </summary>
    [Fact]
    public async Task SendAsync_ResponderThrows_SurfacesTheFailureUnwrappedAndStillRecordsTheRequest()
    {
        // Arrange
        using var handler = new FakeHttpMessageHandler((_, _) => throw new IOException("connection reset"));
        using var client = new HttpClient(handler, disposeHandler: false);

        // Act
        var transportFailure = await Assert.ThrowsAsync<IOException>(
            () => client.GetAsync(RequestUri, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal("connection reset", transportFailure.Message);
        Assert.Single(handler.RecordedRequests);
    }

    [Fact]
    public async Task SendAsync_CallerCancels_PassesTheTokenToTheResponder()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        using var handler = new FakeHttpMessageHandler((_, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });
        using var client = new HttpClient(handler, disposeHandler: false);
        await cancellation.CancelAsync();

        // Act
        var send = async () => await client.GetAsync(RequestUri, cancellation.Token);

        // Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(send);
    }

    /// <summary>
    /// Tests that cover bounded concurrency send in parallel, so the recording must not drop or corrupt a request.
    /// </summary>
    [Fact]
    public async Task RecordedRequests_ConcurrentSends_CaptureEveryRequestExactlyOnce()
    {
        // Arrange
        const int ConcurrentRequestCount = 32;
        using var handler = FakeHttpMessageHandler.AlwaysResponding(() => new HttpResponseMessage(HttpStatusCode.OK));
        using var client = new HttpClient(handler, disposeHandler: false);
        var requestUris = Enumerable.Range(0, ConcurrentRequestCount)
            .Select(index => new Uri(RequestUri, index.ToString(CultureInfo.InvariantCulture)))
            .ToArray();

        // Act
        var responses = await Task.WhenAll(
            requestUris.Select(uri => client.GetAsync(uri, TestContext.Current.CancellationToken)));
        Array.ForEach(responses, response => response.Dispose());

        // Assert
        var recordedUris = handler.RecordedRequests
            .Select(recordedRequest => recordedRequest.RequestUri)
            .OrderBy(uri => uri?.AbsoluteUri, StringComparer.Ordinal);

        Assert.Equal(requestUris.OrderBy(uri => uri.AbsoluteUri, StringComparer.Ordinal), recordedUris);
    }

    [Fact]
    public async Task RecordedRequests_ReadTwiceAroundASend_ReturnsAnIndependentSnapshot()
    {
        // Arrange
        using var handler = FakeHttpMessageHandler.AlwaysResponding(() => new HttpResponseMessage(HttpStatusCode.OK));
        using var client = new HttpClient(handler, disposeHandler: false);
        using var firstResponse = await client.GetAsync(RequestUri, TestContext.Current.CancellationToken);

        // Act
        var snapshotAfterFirstSend = handler.RecordedRequests;
        using var secondResponse = await client.GetAsync(RequestUri, TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(snapshotAfterFirstSend);
        Assert.Equal(2, handler.RecordedRequests.Count);
    }

    [Fact]
    public void RespondingInSequence_NoResponses_RejectsTheEmptyScript()
    {
        // Arrange
        var createHandler = () => FakeHttpMessageHandler.RespondingInSequence();

        // Act
        var exception = Assert.Throws<ArgumentException>(createHandler);

        // Assert
        Assert.Equal("scriptedResponses", exception.ParamName);
    }

    /// <summary>
    /// A script the code under test never reached leaves responses nobody disposed, which under
    /// <c>TreatWarningsAsErrors</c> would otherwise show up as a finalizer-time surprise rather than as cleanup.
    /// </summary>
    [Fact]
    public async Task Dispose_ScriptedResponsesNeverSent_DisposesThem()
    {
        // Arrange
        var unsentResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("unsent", Encoding.UTF8, "text/plain"),
        };
        var handler = FakeHttpMessageHandler.RespondingInSequence(unsentResponse);

        // Act
        handler.Dispose();

        // Assert
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => unsentResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }
}
