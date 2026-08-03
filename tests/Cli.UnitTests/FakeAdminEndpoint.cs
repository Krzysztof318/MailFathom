// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net;
using System.Net.Http.Headers;
using System.Text;
using MailFathom.TestSupport;

namespace MailFathom.Cli.UnitTests;

/// <summary>The administrative endpoint's answers, as a command meets them without a server.</summary>
/// <remarks>
/// Scenarios rather than a handler: the suite has one HTTP test double, shared from <c>tests/shared/</c>, and what is
/// worth naming here is what a deployment does — accepts the credential, refuses it, serves something else, never
/// answers — not a second way of answering a request.
/// </remarks>
internal static class FakeAdminEndpoint
{
    /// <summary>Builds an endpoint that accepts whatever credential it is given.</summary>
    /// <param name="credentialName">The name it reports for the credential.</param>
    /// <param name="version">The version it reports.</param>
    /// <returns>The endpoint.</returns>
    internal static FakeHttpMessageHandler Accepting(string credentialName, string version) => AnsweringBody(
        HttpStatusCode.OK,
        $$"""{"service":"MailFathom","version":"{{version}}","credential":"{{credentialName}}"}""");

    /// <summary>Builds an endpoint that answers with a status and no usable body.</summary>
    /// <param name="status">The status it answers with.</param>
    /// <returns>The endpoint.</returns>
    internal static FakeHttpMessageHandler Answering(HttpStatusCode status) => AnsweringBody(status, string.Empty);

    /// <summary>Builds an endpoint that answers with a status and a body of the caller's choosing.</summary>
    /// <param name="status">The status it answers with.</param>
    /// <param name="body">The body it answers with.</param>
    /// <returns>The endpoint.</returns>
    internal static FakeHttpMessageHandler AnsweringBody(HttpStatusCode status, string body) =>
        FakeHttpMessageHandler.AlwaysResponding(() => new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        });

    /// <summary>Builds an endpoint nothing is listening at.</summary>
    /// <returns>The endpoint.</returns>
    /// <remarks>What a wrong port, a stopped service, or a firewall that refuses looks like from the command's side.</remarks>
    internal static FakeHttpMessageHandler Unreachable() =>
        new((_, _) => throw new HttpRequestException("Connection refused."));

    /// <summary>Builds an endpoint that accepts the connection and never answers.</summary>
    /// <returns>The endpoint.</returns>
    /// <remarks>
    /// What an overloaded deployment or a black-holing firewall looks like, and a different outcome from being
    /// unreachable: the connection succeeded, so the address and the port are right. Raised directly rather than by
    /// waiting out the command's 30-second bound, because a test that spends it to prove a message is a test nobody
    /// runs. <see cref="HttpClient" /> reports its own timeout as a cancelled task, which is what this raises.
    /// </remarks>
    internal static FakeHttpMessageHandler Silent() =>
        new((_, _) => throw new TaskCanceledException("The request timed out."));

    /// <summary>Reports the path of the last request, or <see langword="null" /> when none was sent.</summary>
    /// <param name="endpoint">The endpoint the command was pointed at.</param>
    /// <returns>The path.</returns>
    internal static string? LastPath(this FakeHttpMessageHandler endpoint) =>
        LastRequest(endpoint)?.RequestUri?.AbsolutePath;

    /// <summary>Reports the credential the last request presented, or <see langword="null" /> when it presented none.</summary>
    /// <param name="endpoint">The endpoint the command was pointed at.</param>
    /// <returns>The credential as it was presented.</returns>
    /// <remarks>Half of what these tests assert is the request rather than the answer: which path was reached, and how the credential was presented.</remarks>
    internal static AuthenticationHeaderValue? LastAuthorization(this FakeHttpMessageHandler endpoint) =>
        LastRequest(endpoint)?.Headers.TryGetValue("Authorization", out var presented) == true
            && presented.Count > 0
                ? AuthenticationHeaderValue.Parse(presented[0])
                : null;

    private static RecordedHttpRequest? LastRequest(FakeHttpMessageHandler endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        var observed = endpoint.RecordedRequests;

        return observed.Count == 0 ? null : observed[^1];
    }
}
