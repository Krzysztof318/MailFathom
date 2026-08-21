// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using MailFathom.Cli.Administration;
using MailFathom.TestSupport;
using MailFathom.Versioning;

namespace MailFathom.Cli.UnitTests;

/// <summary>The administrative endpoint's answers, as a command meets them without a server.</summary>
/// <remarks>
/// Scenarios rather than a handler: the suite has one HTTP test double, shared from <c>backend/tests/shared/</c>, and what is
/// worth naming here is what a deployment does — accepts the credential, refuses it, serves something else, never
/// answers — not a second way of answering a request.
/// </remarks>
internal static class FakeAdminEndpoint
{
    /// <summary>Gets the version this command was stamped with, which is what a deployment reports for the two to agree.</summary>
    /// <remarks>
    /// Read from the assembly rather than written as a literal, because the declared prefix moves every release and a
    /// literal would turn the release that moves it into a suite that refuses its own deployment. A test that is about
    /// the version difference names the version it wants; every other one takes this and meets no warning.
    /// </remarks>
    internal static string CommandVersion { get; } =
        StampedAssemblyVersion.ReadFrom(typeof(AdminApiClient).Assembly).Version;

    /// <summary>Gets a version on the release line after this command's, which is the pair every command refuses.</summary>
    internal static string AnotherReleaseLine { get; } = LineAfter(CommandVersion);

    /// <summary>Gets a different build of this command's own line, which is what a nightly of the same release is.</summary>
    internal static string AnotherBuildOfThisLine { get; } = $"{CoreOf(CommandVersion)}-nightly.41";

    /// <summary>Builds an endpoint that accepts whatever credential it is given, reporting this command's own version.</summary>
    /// <param name="credentialName">The name it reports for the credential.</param>
    /// <returns>The endpoint.</returns>
    internal static FakeHttpMessageHandler Accepting(string credentialName) =>
        Accepting(credentialName, CommandVersion);

    /// <summary>Builds an endpoint that accepts whatever credential it is given.</summary>
    /// <param name="credentialName">The name it reports for the credential.</param>
    /// <param name="version">The version it reports.</param>
    /// <returns>The endpoint.</returns>
    internal static FakeHttpMessageHandler Accepting(string credentialName, string version) => AnsweringBody(
        HttpStatusCode.OK,
        SessionBody(credentialName, version));

    /// <summary>Builds an endpoint that accepts the credential and reports the grant it holds.</summary>
    /// <param name="credentialName">The name it reports for the credential.</param>
    /// <param name="permissions">The permissions it reports the credential holding, which is empty for one granted nothing.</param>
    /// <returns>The endpoint.</returns>
    internal static FakeHttpMessageHandler AcceptingWithGrant(
        string credentialName,
        params string[] permissions) =>
        AnsweringBody(HttpStatusCode.OK, SessionBody(credentialName, CommandVersion, permissions));

    /// <summary>Builds the body the session route answers with, stating no grant.</summary>
    /// <param name="credentialName">The name it reports for the credential.</param>
    /// <param name="version">The version it reports.</param>
    /// <returns>The JSON body.</returns>
    /// <remarks>
    /// Shared with the doubles that route by path, so every one of them reports a session the same way and a change to
    /// that shape lands in one place. The grant is left out rather than stated, because most of these scenarios are
    /// about something else and a body carrying one would put a permission list into every assertion about output; a
    /// test whose subject is the grant states it with <see cref="AcceptingWithGrant" />.
    /// </remarks>
    internal static string SessionBody(string credentialName, string version) =>
        $$"""{"service":"MailFathom","version":"{{version}}","credential":"{{credentialName}}"}""";

    /// <summary>Builds the body the session route answers with, stating the grant the credential holds.</summary>
    /// <param name="credentialName">The name it reports for the credential.</param>
    /// <param name="version">The version it reports.</param>
    /// <param name="permissions">The permissions it reports, which is empty for a credential granted nothing.</param>
    /// <returns>The JSON body.</returns>
    internal static string SessionBody(
        string credentialName,
        string version,
        IReadOnlyList<string> permissions)
    {
        ArgumentNullException.ThrowIfNull(permissions);

        var stated = string.Join(',', permissions.Select(permission => $"\"{permission}\""));

        return $$"""
            {"service":"MailFathom","version":"{{version}}","credential":"{{credentialName}}","permissions":[{{stated}}]}
            """;
    }

    /// <summary>Builds one JSON answer, which is the only content type these doubles serve.</summary>
    /// <param name="status">The status it answers with.</param>
    /// <param name="body">The body it answers with.</param>
    /// <returns>The answer.</returns>
    /// <remarks>
    /// Every double that routes by path answers this way, so the encoding and the media type are one decision here
    /// rather than one per command family. It hands back the message rather than a handler, because those doubles
    /// decide which route they are answering before they decide what to say.
    /// </remarks>
    internal static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    /// <summary>Answers the session route the way every command's preflight meets it, or nothing for another route.</summary>
    /// <param name="request">The request the command sent.</param>
    /// <param name="version">The version to report, or <see langword="null" /> for this command's own.</param>
    /// <returns>The session answer, or <see langword="null" /> when the request is for something else.</returns>
    /// <remarks>
    /// <para>
    /// Every command settles the two versions here before its own operation, so each routing double carried the same
    /// branch and a double serving only its own route would report a deployment nothing can be administered on.
    /// Returning <see langword="null" /> rather than a 404 is what lets a double reach it without a second path
    /// comparison of its own.
    /// </para>
    /// <para>
    /// A double reaches it where it would otherwise answer 404, and the position is not a choice about precedence: the
    /// session path is a sibling of every command route under the same prefix, so no route a double claims can be this
    /// one. Written that way rather than as a leading branch because a response assigned to a local before being
    /// returned is a disposable CA2000 cannot see through, while one coalesced into the return is.
    /// </para>
    /// </remarks>
    internal static HttpResponseMessage? AnswerSession(HttpRequestMessage request, string? version = null)
    {
        ArgumentNullException.ThrowIfNull(request);

        return request.RequestUri?.AbsolutePath == AdminEndpointRoutes.SessionPath
            ? Json(HttpStatusCode.OK, SessionBody("workstation", version ?? CommandVersion))
            : null;
    }

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

    private static string LineAfter(string version)
    {
        var line = Version.Parse(CoreOf(version));

        return string.Create(CultureInfo.InvariantCulture, $"{line.Major}.{line.Minor + 1}.0");
    }

    private static string CoreOf(string version) => version.Split('-', '+')[0];

    private static RecordedHttpRequest? LastRequest(FakeHttpMessageHandler endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        var observed = endpoint.RecordedRequests;

        return observed.Count == 0 ? null : observed[^1];
    }
}
