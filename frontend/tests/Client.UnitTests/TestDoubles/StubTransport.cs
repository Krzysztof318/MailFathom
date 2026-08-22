// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace MailFathom.Client.UnitTests.TestDoubles;

/// <summary>A transport that answers from a script instead of from a network.</summary>
/// <remarks>
/// The seam every test in this suite reaches the client through. It is the handler rather than the client, so what is
/// under test is the whole pipeline the registration composes — the token handler included — rather than the one class
/// that happens to send the request.
/// </remarks>
internal sealed class StubTransport : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> answer;

    /// <summary>Initializes a transport answering every request the same way.</summary>
    /// <param name="answer">Produces the answer for a request.</param>
    internal StubTransport(Func<HttpRequestMessage, HttpResponseMessage> answer) => this.answer = answer;

    /// <summary>Gets what was sent, in order, so a test can assert on the request as well as on the answer.</summary>
    internal List<RecordedRequest> Requests { get; } = [];

    /// <summary>Answers every request with one JSON body.</summary>
    /// <param name="json">The body.</param>
    /// <param name="status">The status to answer with.</param>
    /// <returns>The transport.</returns>
    internal static StubTransport AnsweringJson(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(_ => JsonResponse(json, status));

    /// <summary>Answers every request by failing the way the given exception describes.</summary>
    /// <param name="failure">What the transport raises instead of answering.</param>
    /// <returns>The transport.</returns>
    internal static StubTransport Failing(Exception failure) => new(_ => throw failure);

    /// <summary>Builds a JSON answer.</summary>
    /// <param name="json">The body.</param>
    /// <param name="status">The status.</param>
    /// <returns>The answer.</returns>
    internal static HttpResponseMessage JsonResponse(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status)
        {
            Content = new StringContent(json, Encoding.UTF8, new MediaTypeHeaderValue("application/json")),
        };

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        this.Requests.Add(
            new RecordedRequest(
                request.RequestUri!,
                request.Headers.Authorization?.ToString(),
                request.Content is null
                    ? null
                    : await request.Content.ReadAsStringAsync(cancellationToken)));

        return this.answer(request);
    }
}

/// <summary>What one request carried, kept so a test can assert on it after the exchange.</summary>
/// <param name="RequestUri">Where the request went.</param>
/// <param name="Authorization">The authorization header as it was sent, or <see langword="null" /> where none was.</param>
/// <param name="Body">The request body, or <see langword="null" /> where there was none.</param>
internal sealed record RecordedRequest(Uri RequestUri, string? Authorization, string? Body);
