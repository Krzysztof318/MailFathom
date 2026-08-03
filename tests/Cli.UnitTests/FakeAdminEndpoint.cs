// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace MailFathom.Cli.UnitTests;

/// <summary>An administrative endpoint the command can be pointed at without a server.</summary>
/// <remarks>
/// It records what the command sent as well as answering it, because half of what these tests assert is the request:
/// which path was reached and how the credential was presented. A double that only answered would leave both untested.
/// </remarks>
internal sealed class FakeAdminEndpoint : HttpMessageHandler
{
    private readonly HttpStatusCode status;
    private readonly string body;

    private FakeAdminEndpoint(HttpStatusCode status, string body)
    {
        this.status = status;
        this.body = body;
    }

    /// <summary>Gets how many requests the command sent.</summary>
    internal int RequestCount { get; private set; }

    /// <summary>Gets whether a connection to this endpoint succeeds at all.</summary>
    private bool Reachable { get; init; } = true;

    /// <summary>Gets whether this endpoint answers within the command's request timeout.</summary>
    private bool Answers { get; init; } = true;

    /// <summary>Gets the path of the last request, or <see langword="null" /> when none was sent.</summary>
    internal string? LastPath { get; private set; }

    /// <summary>Gets the credential the last request presented, or <see langword="null" /> when it presented none.</summary>
    internal AuthenticationHeaderValue? LastAuthorization { get; private set; }

    /// <summary>Builds an endpoint that accepts whatever credential it is given.</summary>
    /// <param name="credentialName">The name it reports for the credential.</param>
    /// <param name="version">The version it reports.</param>
    /// <returns>The endpoint.</returns>
    internal static FakeAdminEndpoint Accepting(string credentialName, string version) => new(
        HttpStatusCode.OK,
        $$"""{"service":"MailFathom","version":"{{version}}","credential":"{{credentialName}}"}""");

    /// <summary>Builds an endpoint that answers with a status and no usable body.</summary>
    /// <param name="status">The status it answers with.</param>
    /// <returns>The endpoint.</returns>
    internal static FakeAdminEndpoint Answering(HttpStatusCode status) => new(status, string.Empty);

    /// <summary>Builds an endpoint nothing is listening at.</summary>
    /// <returns>The endpoint.</returns>
    /// <remarks>What a wrong port, a stopped service, or a firewall looks like from the command's side.</remarks>
    internal static FakeAdminEndpoint Unreachable() => new(HttpStatusCode.OK, string.Empty) { Reachable = false };

    /// <summary>Builds an endpoint that accepts the connection and never answers.</summary>
    /// <returns>The endpoint.</returns>
    /// <remarks>
    /// What an overloaded deployment or a black-holing firewall looks like, and a different outcome from being
    /// unreachable: the connection succeeded, so the address and the port are right and the operator's next move is
    /// elsewhere. <see cref="HttpClient" /> reports its own timeout as a cancelled task, which is what this raises.
    /// </remarks>
    internal static FakeAdminEndpoint Silent() => new(HttpStatusCode.OK, string.Empty) { Answers = false };

    /// <summary>Builds an endpoint that answers with a status and a body of the caller's choosing.</summary>
    /// <param name="status">The status it answers with.</param>
    /// <param name="body">The body it answers with.</param>
    /// <returns>The endpoint.</returns>
    internal static FakeAdminEndpoint AnsweringBody(HttpStatusCode status, string body) => new(status, body);

    /// <inheritdoc />
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        this.RequestCount++;
        this.LastPath = request.RequestUri?.AbsolutePath;
        this.LastAuthorization = request.Headers.Authorization;

        if (!this.Reachable)
        {
            throw new HttpRequestException("Connection refused.");
        }

        if (!this.Answers)
        {
            // Raised directly rather than by waiting out a real timeout, because a test that spends the command's
            // 30-second bound to prove the message is a test nobody runs. The caller's own token is not cancelled,
            // which is what separates this from an operator pressing Ctrl-C.
            throw new TaskCanceledException("The request timed out.");
        }

        return Task.FromResult(new HttpResponseMessage(this.status)
        {
            Content = new StringContent(this.body, Encoding.UTF8, "application/json"),
        });
    }
}
