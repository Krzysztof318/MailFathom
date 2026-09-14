// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using System.Net;
using System.Text.Json;
using MailFathom.Cli.Administration;
using MailFathom.TestSupport;

namespace MailFathom.Cli.UnitTests;

/// <summary>A deployment answering the user roster and the mail account routes, holding one user and one account assigned to them.</summary>
/// <remarks>
/// The roster is answered by every shape here, because the commands that name a user settle which one they act for
/// before anything else. What each shape varies is what a write answers with and what ending an assignment did.
/// </remarks>
internal static class FakeMailAccountDeployment
{
    /// <summary>Gets the version the account reports, which a save is composed over.</summary>
    internal const long AccountVersion = 2;

    /// <summary>The declaration the account reports, as the deployment would redact it.</summary>
    internal const string Declaration =
        """{"EmailAddress":"alex@example.test","DisplayName":"Work","Host":"imap.example.test"}""";

    /// <summary>Gets the one user the roster reports.</summary>
    internal static Guid User { get; } = new("11111111-1111-1111-1111-111111111111");

    /// <summary>Gets the account the deployment holds.</summary>
    internal static Guid Account { get; } = new("66666666-6666-6666-6666-666666666666");

    /// <summary>Gets the identifier a creation reports, which is the one thing a script cannot reconstruct from what it typed.</summary>
    internal static Guid CreatedAccount { get; } = new("77777777-7777-7777-7777-777777777777");

    /// <summary>Builds a deployment that commits every write and ends an assignment others still share.</summary>
    /// <returns>The deployment.</returns>
    internal static FakeHttpMessageHandler Holding() => Answering(
        string.Create(
            CultureInfo.InvariantCulture,
            $$"""{"committed":true,"version":1,"code":null,"messages":[],"accountId":"{{CreatedAccount:D}}"}"""),
        Committed,
        """{"unassigned":true,"accountErased":false}""");

    /// <summary>Builds a deployment holding more accounts than one listing carries, of which it lists the one.</summary>
    /// <returns>The deployment.</returns>
    internal static FakeHttpMessageHandler HoldingMoreThanOneListing() => Answering(
        Committed,
        Committed,
        """{"unassigned":true,"accountErased":false}""",
        truncated: true);

    /// <summary>Builds a deployment whose last assignment to the account is the one being ended.</summary>
    /// <returns>The deployment.</returns>
    internal static FakeHttpMessageHandler ErasingOnTheLastUnassignment() => Answering(
        Committed,
        Committed,
        """{"unassigned":true,"accountErased":true}""");

    /// <summary>Builds a deployment that refuses every write, with the code and the sentence it names.</summary>
    /// <param name="code">The five-digit code the refusal carries.</param>
    /// <param name="message">The sentence the refusal carries.</param>
    /// <returns>The deployment.</returns>
    internal static FakeHttpMessageHandler RefusingTheWrite(int code, string message)
    {
        var refusal = string.Create(
            CultureInfo.InvariantCulture,
            $$"""{"committed":false,"version":{{AccountVersion}},"code":{{code}},"messages":[{{JsonSerializer.Serialize(message)}}],"accountId":null}""");

        return Answering(refusal, refusal, """{"unassigned":false,"accountErased":false}""");
    }

    private static string Committed => string.Create(
        CultureInfo.InvariantCulture,
        $$"""{"committed":true,"version":{{AccountVersion + 1}},"code":null,"messages":[],"accountId":null}""");

    private static FakeHttpMessageHandler Answering(
        string creationAnswer,
        string writeAnswer,
        string unassignment,
        bool truncated = false) =>
        new((request, _) => Task.FromResult(Answer(request, creationAnswer, writeAnswer, unassignment, truncated)));

    private static HttpResponseMessage Answer(
        HttpRequestMessage request,
        string creationAnswer,
        string writeAnswer,
        string unassignment,
        bool truncated)
    {
        var path = request.RequestUri?.AbsolutePath ?? string.Empty;

        return path switch
        {
            AdminEndpointRoutes.UsersPath => FakeAdminEndpoint.Json(
                HttpStatusCode.OK,
                $$"""{"users":[{"id":"{{User:D}}","displayName":"alex","served":true,"mcpEndpoint":true,"clientEndpoint":true}]}"""),
            AdminEndpointRoutes.MailAccountsPath => request.Method == HttpMethod.Get
                ? FakeAdminEndpoint.Json(HttpStatusCode.OK, $$"""{"accounts":[{{Summary()}}],"truncated":{{(truncated ? "true" : "false")}}}""")
                : FakeAdminEndpoint.Json(HttpStatusCode.OK, creationAnswer),
            _ when path == AdminEndpointRoutes.MailAccountPath(Account) => AnswerAccount(request, writeAnswer),
            _ when path == AdminEndpointRoutes.MailAccountAssignmentsPath(Account) =>
                FakeAdminEndpoint.Json(HttpStatusCode.OK, writeAnswer),
            _ when path == AdminEndpointRoutes.MailAccountAssignmentRemovalPath(Account) =>
                FakeAdminEndpoint.Json(HttpStatusCode.OK, unassignment),
            _ => FakeAdminEndpoint.AnswerSession(request) ?? FakeAdminEndpoint.Json(HttpStatusCode.NotFound, string.Empty),
        };
    }

    private static HttpResponseMessage AnswerAccount(HttpRequestMessage request, string writeAnswer) =>
        request.Method == HttpMethod.Get ? FakeAdminEndpoint.Json(HttpStatusCode.OK, Entry())
        : request.Method == HttpMethod.Delete ? FakeAdminEndpoint.Json(HttpStatusCode.OK, """{"erased":true}""")
        : FakeAdminEndpoint.Json(HttpStatusCode.OK, writeAnswer);

    private static string Summary() => string.Create(
        CultureInfo.InvariantCulture,
        $$"""{"id":"{{Account:D}}","version":{{AccountVersion}},"users":["{{User:D}}"],"emailAddress":"alex@example.test","displayName":"Work"}""");

    private static string Entry() => string.Create(
        CultureInfo.InvariantCulture,
        $$"""{"id":"{{Account:D}}","version":{{AccountVersion}},"users":["{{User:D}}"],"declaration":{{JsonSerializer.Serialize(Declaration)}}}""");
}
