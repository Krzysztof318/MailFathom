// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using System.Net;
using MailFathom.Cli.Administration;
using MailFathom.TestSupport;

namespace MailFathom.Cli.UnitTests;

/// <summary>A deployment answering the owner roster and the five credential routes.</summary>
/// <remarks>
/// The roster is answered by every shape here rather than only by the tests about it, because every credential command
/// settles which owner it acts for before doing anything else — a double serving only its own route would fail each of
/// them on a 404 that says nothing about what the command was doing.
/// </remarks>
internal static class FakeOwnerCredentialDeployment
{
    private const string Provisioned = "2026-08-20T09:00:00+00:00";

    private const string PasswordChanged = "2026-08-24T17:30:00+00:00";

    /// <summary>Gets the identifier this deployment reports for a credential it has just provisioned.</summary>
    internal static Guid ProvisionedCredentialId { get; } = new("44444444-4444-4444-4444-444444444444");

    /// <summary>Builds a deployment holding the owners named, and the credentials each of them holds.</summary>
    /// <param name="owners">The owners the roster reports, in the order it serves them.</param>
    /// <param name="credentials">What a listing of any owner's credentials answers with.</param>
    /// <returns>The deployment.</returns>
    internal static FakeHttpMessageHandler Holding(IReadOnlyList<Guid> owners, params string[] credentials) =>
        new((request, _) => Task.FromResult(Answer(request, owners, credentials, HttpStatusCode.OK, string.Empty)));

    /// <summary>Builds a deployment that refuses whatever write is asked of it.</summary>
    /// <param name="owners">The owners the roster reports.</param>
    /// <param name="status">The status the write is refused with.</param>
    /// <param name="detail">What the refusal says, as the problem document carries it.</param>
    /// <returns>The deployment.</returns>
    internal static FakeHttpMessageHandler Refusing(
        IReadOnlyList<Guid> owners,
        HttpStatusCode status,
        string detail) =>
        new((request, _) => Task.FromResult(Answer(request, owners, [], status, detail)));

    /// <summary>Writes one credential as a listing carries it.</summary>
    /// <param name="id">The identifier the deployment gave the credential.</param>
    /// <param name="username">The name the owner signs in with.</param>
    /// <param name="enabled">Whether it still authenticates requests.</param>
    /// <returns>The credential, as an element of the listing's array.</returns>
    internal static string Credential(Guid id, string username, bool enabled = true) => string.Create(
        CultureInfo.InvariantCulture,
        $$"""{"id":"{{id:D}}","username":"{{username}}","enabled":{{(enabled ? "true" : "false")}},"createdAt":"{{Provisioned}}","passwordChangedAt":"{{PasswordChanged}}"}""");

    /// <summary>Reports the requests the command sent to one path under one method.</summary>
    /// <param name="deployment">The deployment the command was pointed at.</param>
    /// <param name="method">The verb to count.</param>
    /// <param name="path">The path to count.</param>
    /// <returns>The requests, in the order they were sent.</returns>
    internal static IReadOnlyList<RecordedHttpRequest> RequestsTo(
        this FakeHttpMessageHandler deployment,
        HttpMethod method,
        string path)
    {
        ArgumentNullException.ThrowIfNull(deployment);

        return [.. deployment.RecordedRequests.Where(request =>
            request.Method == method && request.RequestUri?.AbsolutePath == path)];
    }

    private static HttpResponseMessage Answer(
        HttpRequestMessage request,
        IReadOnlyList<Guid> owners,
        IReadOnlyList<string> credentials,
        HttpStatusCode writeStatus,
        string detail)
    {
        var path = request.RequestUri?.AbsolutePath ?? string.Empty;

        if (path == AdminEndpointRoutes.OwnersPath)
        {
            return FakeAdminEndpoint.Json(
                HttpStatusCode.OK,
                $$"""{"owners":[{{string.Join(',', owners.Select(owner => $"\"{owner:D}\""))}}]}""");
        }

        if (path.EndsWith("/credentials", StringComparison.Ordinal))
        {
            return request.Method == HttpMethod.Get
                ? FakeAdminEndpoint.Json(
                    HttpStatusCode.OK,
                    $$"""{"owner":"{{(owners.Count > 0 ? owners[0] : Guid.Empty):D}}","credentials":[{{string.Join(',', credentials)}}]}""")
                : Written(writeStatus, detail, $$"""{"credentialId":"{{ProvisionedCredentialId:D}}"}""");
        }

        if (path.Contains("/credentials/", StringComparison.Ordinal))
        {
            return Written(writeStatus, detail, string.Empty);
        }

        return FakeAdminEndpoint.AnswerSession(request)
            ?? FakeAdminEndpoint.Json(HttpStatusCode.NotFound, string.Empty);
    }

    private static HttpResponseMessage Written(HttpStatusCode status, string detail, string body) =>
        status == HttpStatusCode.OK
            ? FakeAdminEndpoint.Json(HttpStatusCode.OK, body)
            : FakeAdminEndpoint.Json(status, $$"""{"detail":"{{detail}}"}""");
}
