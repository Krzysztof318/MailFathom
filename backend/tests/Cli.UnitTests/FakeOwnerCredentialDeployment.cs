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

    private const string MaterialChanged = "2026-08-24T17:30:00+00:00";

    /// <summary>Gets the identifier this deployment reports for a credential it has just provisioned.</summary>
    internal static Guid ProvisionedCredentialId { get; } = new("44444444-4444-4444-4444-444444444444");



    /// <summary>Builds a deployment holding the owners named, and the credentials each of them holds.</summary>
    /// <param name="owners">The owners the roster reports, in the order it serves them.</param>
    /// <param name="credentials">What a listing of any owner's credentials answers with.</param>
    /// <returns>The deployment.</returns>
    internal static FakeHttpMessageHandler Holding(IReadOnlyList<Guid> owners, params string[] credentials) =>
        new((request, _) => Task.FromResult(Answer(
            request,
            owners,
            credentials,
            HttpStatusCode.OK,
            string.Empty,
            "owner",
            mintedKey: null)));

    /// <summary>A deployment answering a provisioning with what the client presents, which is a minted key for the methods that mint one.</summary>
    /// <param name="owners">The owners the roster reports.</param>
    /// <param name="provisionedLookup">What the deployment reports the credential is resolved by.</param>
    /// <param name="mintedKey">The plaintext a minting method answers with, and <see langword="null" /> where the method mints nothing.</param>
    /// <returns>The handler.</returns>
    /// <remarks>
    /// A name of its own rather than an overload of <see cref="Holding(IReadOnlyList{Guid}, string[])" />, because the
    /// two would differ only in how many strings follow the roster: a call listing two credentials binds to the fixed
    /// parameters instead of the array, and the listing then answers with nothing while the arrangement looks right.
    /// </remarks>
    internal static FakeHttpMessageHandler Provisioning(
        IReadOnlyList<Guid> owners,
        string provisionedLookup,
        string? mintedKey) =>
        new((request, _) => Task.FromResult(Answer(
            request,
            owners,
            [],
            HttpStatusCode.OK,
            string.Empty,
            provisionedLookup,
            mintedKey)));

    /// <summary>Builds a deployment that refuses whatever write is asked of it.</summary>
    /// <param name="owners">The owners the roster reports.</param>
    /// <param name="status">The status the write is refused with.</param>
    /// <param name="detail">What the refusal says, as the problem document carries it.</param>
    /// <returns>The deployment.</returns>
    internal static FakeHttpMessageHandler Refusing(
        IReadOnlyList<Guid> owners,
        HttpStatusCode status,
        string detail) =>
        new((request, _) => Task.FromResult(Answer(request, owners, [], status, detail, "owner", mintedKey: null)));

    /// <summary>Answers every credential listing with a refusal while every write is accepted.</summary>
    /// <param name="owners">The owners the deployment holds records for.</param>
    /// <param name="detail">What the refusal says, as the problem document carries it.</param>
    /// <returns>The deployment.</returns>
    /// <remarks>Reading and removing are separately granted, so a token holding the write grant and not the read one is an ordinary arrangement rather than a broken deployment.</remarks>
    internal static FakeHttpMessageHandler RefusingTheListing(IReadOnlyList<Guid> owners, string detail) =>
        new((request, _) => Task.FromResult(
            request.Method == HttpMethod.Get
            && (request.RequestUri?.AbsolutePath ?? string.Empty).EndsWith("/credentials", StringComparison.Ordinal)
                ? FakeAdminEndpoint.Json(HttpStatusCode.Forbidden, $$"""{"detail":"{{detail}}"}""")
                : Answer(request, owners, [], HttpStatusCode.OK, string.Empty, "owner", mintedKey: null)));

    /// <summary>Writes one credential as a listing carries it.</summary>
    /// <param name="id">The identifier the deployment gave the credential.</param>
    /// <param name="lookup">What the credential is resolved by, and <see langword="null" /> where the value is derived from the secret and therefore withheld.</param>
    /// <param name="method">How the credential is presented, by the name the method publishes.</param>
    /// <param name="enabled">Whether it still authenticates requests.</param>
    /// <param name="permissions">What the credential grants, by permission name.</param>
    /// <returns>The credential, as an element of the listing's array.</returns>
    internal static string Credential(
        Guid id,
        string? lookup,
        string method = "password",
        bool enabled = true,
        params string[] permissions) => string.Create(
        CultureInfo.InvariantCulture,
        $$"""{"id":"{{id:D}}","method":"{{method}}","lookup":{{Written(lookup)}},"permissions":[{{string.Join(',', permissions.Select(permission => $"\"{permission}\""))}}],"enabled":{{(enabled ? "true" : "false")}},"createdAt":"{{Provisioned}}","materialChangedAt":"{{MaterialChanged}}"}""");

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
        string detail,
        string provisionedLookup,
        string? mintedKey)
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
                : Written(
                    writeStatus,
                    detail,
                    $$"""{"credentialId":"{{ProvisionedCredentialId:D}}","lookup":"{{provisionedLookup}}","key":{{Written(mintedKey)}}}""");
        }

        if (path.EndsWith("/material", StringComparison.Ordinal))
        {
            return Written(
                writeStatus,
                detail,
                $$"""{"lookup":"{{provisionedLookup}}","key":{{Written(mintedKey)}}}""");
        }

        if (path.Contains("/credentials/", StringComparison.Ordinal))
        {
            return Written(writeStatus, detail, string.Empty);
        }

        return FakeAdminEndpoint.AnswerSession(request)
            ?? FakeAdminEndpoint.Json(HttpStatusCode.NotFound, string.Empty);
    }

    private static string Written(string? value) => value is null ? "null" : $"\"{value}\"";

    private static HttpResponseMessage Written(HttpStatusCode status, string detail, string body) =>
        status == HttpStatusCode.OK
            ? FakeAdminEndpoint.Json(HttpStatusCode.OK, body)
            : FakeAdminEndpoint.Json(status, $$"""{"detail":"{{detail}}"}""");
}
