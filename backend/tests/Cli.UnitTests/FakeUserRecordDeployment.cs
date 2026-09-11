// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using System.Net;
using System.Text.Json;
using MailFathom.Cli.Administration;
using MailFathom.TestSupport;

namespace MailFathom.Cli.UnitTests;

/// <summary>A deployment answering the roster and the five user-record routes.</summary>
/// <remarks>
/// Every user command settles which user it acts for before doing anything else, so the roster is answered by every
/// shape here rather than only by the tests about it. What each shape varies is the one thing the command under test
/// reads: the record, what a write answers with, and whether the deployment holds the user at all.
/// </remarks>
internal static class FakeUserRecordDeployment
{
    /// <summary>Gets the version every record here reports, which a write is composed over.</summary>
    internal const long RecordVersion = 3;

    /// <summary>The record a deployment answers with where a suite says nothing about one.</summary>
    private const string EmptyRecord = "{}";

    /// <summary>Builds a deployment holding the users stated, each with an empty record.</summary>
    /// <param name="users">The users the roster reports, in the order it serves them.</param>
    /// <returns>The deployment.</returns>
    internal static FakeHttpMessageHandler Holding(params Guid[] users) =>
        Answering(users, WriteCommitted, records: [EmptyRecord]);

    /// <summary>Builds a deployment whose one user's record is the document stated.</summary>
    /// <param name="user">The user the roster reports.</param>
    /// <param name="records">What each successive read of the record answers with, the last one repeating.</param>
    /// <returns>The deployment.</returns>
    /// <remarks>
    /// More than one record is how a writer committing between the read and the write is arranged: the buffer is opened
    /// over the first, and the reading that reports what moved meets the next.
    /// </remarks>
    internal static FakeHttpMessageHandler HoldingRecords(Guid user, params string[] records) =>
        Answering([user], WriteCommitted, records);

    /// <summary>Builds a deployment that refuses every write to a record, with the code and the sentence it names.</summary>
    /// <param name="user">The user the roster reports.</param>
    /// <param name="code">The five-digit code the refusal carries.</param>
    /// <param name="message">The sentence the refusal carries.</param>
    /// <param name="records">What each successive read of the record answers with, the last one repeating.</param>
    /// <returns>The deployment.</returns>
    internal static FakeHttpMessageHandler RefusingTheWrite(
        Guid user,
        int code,
        string message,
        params string[] records) =>
        Answering(
            [user],
            string.Create(
                CultureInfo.InvariantCulture,
                $$"""{"committed":false,"version":{{RecordVersion}},"code":{{code}},"messages":["{{message}}"]}"""),
            records.Length == 0 ? [EmptyRecord] : records);

    /// <summary>Builds a deployment holding no user at all.</summary>
    /// <returns>The deployment.</returns>
    internal static FakeHttpMessageHandler HoldingNobody() =>
        Answering([], WriteCommitted, records: [EmptyRecord]);

    /// <summary>Reports the requests the command sent to one path under one method.</summary>
    /// <param name="deployment">The deployment the command was pointed at.</param>
    /// <param name="method">The verb to count.</param>
    /// <param name="path">The path to count.</param>
    /// <returns>The requests, in the order they were sent.</returns>
    internal static IReadOnlyList<RecordedHttpRequest> UserRequestsTo(
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

    /// <summary>Builds the deployment every shape above is one arrangement of.</summary>
    /// <remarks>The count of record reads is the deployment's own rather than a parameter, so a suite arranging several records states them and nothing else.</remarks>
    private static FakeHttpMessageHandler Answering(
        IReadOnlyList<Guid> users,
        string writeAnswer,
        string[] records)
    {
        var reads = 0;

        // Advanced where the record is read rather than per request, so a suite's second record is what the second
        // reading of the record meets rather than whatever the command happened to ask for next.
        string NextRecord() => records[Math.Min(reads++, records.Length - 1)];

        return new((request, _) => Task.FromResult(Answer(request, users, writeAnswer, NextRecord)));
    }

    private static HttpResponseMessage Answer(
        HttpRequestMessage request,
        IReadOnlyList<Guid> users,
        string writeAnswer,
        Func<string> nextRecord)
    {
        var path = request.RequestUri?.AbsolutePath ?? string.Empty;

        if (path == AdminEndpointRoutes.UsersPath)
        {
            return request.Method == HttpMethod.Get
                ? FakeAdminEndpoint.Json(
                    HttpStatusCode.OK,
                    $$"""{"users":[{{string.Join(',', users.Select(Roster))}}]}""")
                : FakeAdminEndpoint.Json(
                    HttpStatusCode.OK,
                    $$"""{"id":"{{ProvisionedUser:D}}"}""");
        }

        if (users.Count > 0 && path == AdminEndpointRoutes.UserPath(users[0]))
        {
            return FakeAdminEndpoint.Json(HttpStatusCode.OK, """{"erased":true,"wasServed":true}""");
        }

        if (path.EndsWith("/display-name", StringComparison.Ordinal))
        {
            // Acceptance is the whole answer, so the deployment sends no body and the command has nothing to read.
            return FakeAdminEndpoint.Json(HttpStatusCode.NoContent, string.Empty);
        }

        if (path.Contains("/record/mail-accounts", StringComparison.Ordinal))
        {
            return FakeAdminEndpoint.Json(HttpStatusCode.OK, writeAnswer);
        }

        if (path.EndsWith("/record", StringComparison.Ordinal))
        {
            return request.Method == HttpMethod.Get
                ? FakeAdminEndpoint.Json(HttpStatusCode.OK, Record(users, nextRecord()))
                : FakeAdminEndpoint.Json(HttpStatusCode.OK, writeAnswer);
        }

        return FakeAdminEndpoint.AnswerSession(request)
            ?? FakeAdminEndpoint.Json(HttpStatusCode.NotFound, string.Empty);
    }

    /// <summary>The identifier a provisioning reports, which is the one thing a script cannot reconstruct from what it typed.</summary>
    private static Guid ProvisionedUser { get; } = new("55555555-5555-5555-5555-555555555555");

    private static string Roster(Guid user) =>
        $$"""{"id":"{{user:D}}","displayName":"user-{{user:D}}","served":true}""";

    private static string Record(IReadOnlyList<Guid> users, string document) => string.Create(
        CultureInfo.InvariantCulture,
        $$"""
          {"user":"{{(users.Count > 0 ? users[0] : Guid.Empty):D}}","displayName":"user-{{(users.Count > 0 ? users[0] : Guid.Empty):D}}",
          "version":{{RecordVersion}},"document":{{JsonSerializer.Serialize(document)}}}
          """);
}
