// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using System.Net;
using MailFathom.Cli.Administration;
using MailFathom.TestSupport;

namespace MailFathom.Cli.UnitTests;

/// <summary>A deployment answering the roster and the five owner-record routes.</summary>
/// <remarks>
/// Every owner command settles which owner it acts for before doing anything else, so the roster is answered by every
/// shape here rather than only by the tests about it. What each shape varies is the one thing the command under test
/// reads: where the owner's mail accounts come from, what a write answers with, and whether the deployment holds the
/// owner at all.
/// </remarks>
internal static class FakeOwnerRecordDeployment
{
    /// <summary>Gets the version every record here reports, which a write is composed over.</summary>
    internal const long RecordVersion = 3;

    /// <summary>Builds a deployment whose owners read their mail accounts from their own records.</summary>
    /// <param name="owners">The owners the roster reports, in the order it serves them.</param>
    /// <returns>The deployment.</returns>
    internal static FakeHttpMessageHandler Holding(params Guid[] owners) =>
        Answering(owners, readFromConfiguration: false, WriteCommitted, adoptable: []);

    /// <summary>Builds a deployment one of whose owners is still supplied by a configuration source.</summary>
    /// <param name="owner">The owner the configuration supplies.</param>
    /// <param name="mailAccounts">The mail accounts an adoption would move.</param>
    /// <returns>The deployment.</returns>
    internal static FakeHttpMessageHandler SupplyingFromConfiguration(Guid owner, params string[] mailAccounts) =>
        Answering([owner], readFromConfiguration: true, WriteCommitted, mailAccounts);

    /// <summary>Builds a deployment that refuses every write to a record, with the code and the sentence it names.</summary>
    /// <param name="owner">The owner the roster reports.</param>
    /// <param name="code">The five-digit code the refusal carries.</param>
    /// <param name="message">The sentence the refusal carries.</param>
    /// <returns>The deployment.</returns>
    internal static FakeHttpMessageHandler RefusingTheWrite(Guid owner, int code, string message) =>
        Answering(
            [owner],
            readFromConfiguration: false,
            string.Create(
                CultureInfo.InvariantCulture,
                $$"""{"committed":false,"version":{{RecordVersion}},"code":{{code}},"messages":["{{message}}"]}"""),
            adoptable: []);

    /// <summary>Builds a deployment holding no owner at all.</summary>
    /// <returns>The deployment.</returns>
    internal static FakeHttpMessageHandler HoldingNobody() =>
        Answering([], readFromConfiguration: false, WriteCommitted, adoptable: []);

    /// <summary>Reports the requests the command sent to one path under one method.</summary>
    /// <param name="deployment">The deployment the command was pointed at.</param>
    /// <param name="method">The verb to count.</param>
    /// <param name="path">The path to count.</param>
    /// <returns>The requests, in the order they were sent.</returns>
    internal static IReadOnlyList<RecordedHttpRequest> OwnerRequestsTo(
        this FakeHttpMessageHandler deployment,
        HttpMethod method,
        string path)
    {
        ArgumentNullException.ThrowIfNull(deployment);

        return
        [
            .. deployment.RecordedRequests.Where(request =>
                request.Method == method && request.RequestUri?.AbsolutePath == path),
        ];
    }

    /// <summary>The answer every committed write reports, which moves the record one version on.</summary>
    private static string WriteCommitted => string.Create(
        CultureInfo.InvariantCulture,
        $$"""{"committed":true,"version":{{RecordVersion + 1}},"messages":[]}""");

    private static FakeHttpMessageHandler Answering(
        IReadOnlyList<Guid> owners,
        bool readFromConfiguration,
        string writeAnswer,
        IReadOnlyList<string> adoptable) =>
        new((request, _) => Task.FromResult(
            Answer(request, owners, readFromConfiguration, writeAnswer, adoptable)));

    private static HttpResponseMessage Answer(
        HttpRequestMessage request,
        IReadOnlyList<Guid> owners,
        bool readFromConfiguration,
        string writeAnswer,
        IReadOnlyList<string> adoptable)
    {
        var path = request.RequestUri?.AbsolutePath ?? string.Empty;

        if (path == AdminEndpointRoutes.OwnersPath)
        {
            return request.Method == HttpMethod.Get
                ? FakeAdminEndpoint.Json(
                    HttpStatusCode.OK,
                    $$"""{"owners":[{{string.Join(',', owners.Select(owner => Roster(owner, readFromConfiguration)))}}]}""")
                : FakeAdminEndpoint.Json(
                    HttpStatusCode.OK,
                    $$"""{"id":"{{ProvisionedOwner:D}}"}""");
        }

        if (owners.Count > 0 && path == AdminEndpointRoutes.OwnerPath(owners[0]))
        {
            return FakeAdminEndpoint.Json(HttpStatusCode.OK, """{"erased":true,"wasServed":true}""");
        }

        if (path.EndsWith("/display-name", StringComparison.Ordinal))
        {
            // Acceptance is the whole answer, so the deployment sends no body and the command has nothing to read.
            return FakeAdminEndpoint.Json(HttpStatusCode.NoContent, string.Empty);
        }

        if (path.EndsWith("/record/adoption", StringComparison.Ordinal))
        {
            return request.Method == HttpMethod.Get
                ? FakeAdminEndpoint.Json(HttpStatusCode.OK, AdoptionPreview(owners, readFromConfiguration, adoptable))
                : FakeAdminEndpoint.Json(HttpStatusCode.OK, writeAnswer);
        }

        if (path.Contains("/record/mail-accounts", StringComparison.Ordinal))
        {
            return FakeAdminEndpoint.Json(HttpStatusCode.OK, writeAnswer);
        }

        if (path.EndsWith("/record", StringComparison.Ordinal))
        {
            return request.Method == HttpMethod.Get
                ? FakeAdminEndpoint.Json(HttpStatusCode.OK, Record(owners, readFromConfiguration))
                : FakeAdminEndpoint.Json(HttpStatusCode.OK, writeAnswer);
        }

        return FakeAdminEndpoint.AnswerSession(request)
            ?? FakeAdminEndpoint.Json(HttpStatusCode.NotFound, string.Empty);
    }

    /// <summary>The identifier a provisioning reports, which is the one thing a script cannot reconstruct from what it typed.</summary>
    private static Guid ProvisionedOwner { get; } = new("55555555-5555-5555-5555-555555555555");

    private static string Roster(Guid owner, bool readFromConfiguration) => string.Create(
        CultureInfo.InvariantCulture,
        $$"""{"id":"{{owner:D}}","displayName":"owner-{{owner:D}}","recordIsTheirOwn":{{Flag(!readFromConfiguration)}},"declaredInConfiguration":{{Flag(readFromConfiguration)}},"served":true}""");

    private static string Record(IReadOnlyList<Guid> owners, bool readFromConfiguration) => string.Create(
        CultureInfo.InvariantCulture,
        $$"""
          {"owner":"{{(owners.Count > 0 ? owners[0] : Guid.Empty):D}}","displayName":"owner-{{(owners.Count > 0 ? owners[0] : Guid.Empty):D}}",
          "version":{{RecordVersion}},"source":"{{(readFromConfiguration ? "DeploymentSection" : "OwnerDocument")}}",
          "readFromConfiguration":{{Flag(readFromConfiguration)}},"document":"{}"}
          """);

    private static string AdoptionPreview(
        IReadOnlyList<Guid> owners,
        bool readFromConfiguration,
        IReadOnlyList<string> adoptable) => string.Create(
        CultureInfo.InvariantCulture,
        $$"""
          {"owner":"{{(owners.Count > 0 ? owners[0] : Guid.Empty):D}}","displayName":"owner-{{(owners.Count > 0 ? owners[0] : Guid.Empty):D}}",
          "version":{{RecordVersion}},"source":"{{(readFromConfiguration ? "DeploymentSection" : "OwnerDocument")}}",
          "readFromConfiguration":{{Flag(readFromConfiguration)}},
          "configurationPath":{{(readFromConfiguration ? "\"MailSynchronization:Accounts\"" : "null")}},
          "mailAccounts":[{{string.Join(',', adoptable.Select(accountId => $$"""{"accountId":"{{accountId}}","displayName":"{{accountId}} at work"}"""))}}]}
          """);

    private static string Flag(bool value) => value ? "true" : "false";
}
