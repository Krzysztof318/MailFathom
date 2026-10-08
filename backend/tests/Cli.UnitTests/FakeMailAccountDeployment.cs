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

    /// <summary>The cursor the first page of a listing spread over two pages names the second by.</summary>
    private const string SecondPageCursor = "second-page";

    /// <summary>How the account listing answers.</summary>
    private enum ListingShape
    {
        /// <summary>Every account on one page.</summary>
        OnePage = 0,

        /// <summary>One account per page, the first naming the second.</summary>
        TwoPages = 1,

        /// <summary>Two pages, the second naming itself again.</summary>
        RepeatingItsCursor = 2,
    }

    /// <summary>Gets the one user the roster reports.</summary>
    internal static Guid User { get; } = new("11111111-1111-1111-1111-111111111111");

    /// <summary>Gets the account the deployment holds.</summary>
    internal static Guid Account { get; } = new("66666666-6666-6666-6666-666666666666");

    /// <summary>Gets the organization the account belongs to.</summary>
    internal static Guid Organization { get; } = new("88888888-8888-4888-8888-888888888888");

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

    /// <summary>Gets the account the second page of a listing spread over two pages holds.</summary>
    internal static Guid SecondAccount { get; } = new("88888888-8888-8888-8888-888888888888");

    /// <summary>Builds a deployment whose listing answers its accounts on two pages, the first naming the cursor of the second.</summary>
    /// <returns>The deployment.</returns>
    internal static FakeHttpMessageHandler HoldingAccountsOnTwoPages() => Answering(
        Committed,
        Committed,
        """{"unassigned":true,"accountErased":false}""",
        ListingShape.TwoPages);

    /// <summary>Builds a deployment whose listing answers its second page with the cursor that asked for it, so following it would never end.</summary>
    /// <returns>The deployment.</returns>
    internal static FakeHttpMessageHandler RepeatingTheListingCursor() => Answering(
        Committed,
        Committed,
        """{"unassigned":true,"accountErased":false}""",
        ListingShape.RepeatingItsCursor);

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
        ListingShape listing = ListingShape.OnePage) =>
        new((request, _) => Task.FromResult(Answer(request, creationAnswer, writeAnswer, unassignment, listing)));

    private static HttpResponseMessage Answer(
        HttpRequestMessage request,
        string creationAnswer,
        string writeAnswer,
        string unassignment,
        ListingShape listing)
    {
        var path = request.RequestUri?.AbsolutePath ?? string.Empty;

        return path switch
        {
            AdminEndpointRoutes.UsersPath => FakeAdminEndpoint.Json(
                HttpStatusCode.OK,
                $$"""{"users":[{"id":"{{User:D}}","displayName":"alex","served":true,"mcpEndpoint":true,"clientEndpoint":true}]}"""),
            AdminEndpointRoutes.MailAccountsPath => request.Method == HttpMethod.Get
                ? FakeAdminEndpoint.Json(HttpStatusCode.OK, ListingPage(request, listing))
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

    /// <summary>Answers one page of the listing in the shape the deployment was built with.</summary>
    private static string ListingPage(HttpRequestMessage request, ListingShape shape)
    {
        if (shape == ListingShape.OnePage)
        {
            return $$"""{"accounts":[{{Summary()}}],"nextCursor":null}""";
        }

        if (request.RequestUri?.Query != $"?cursor={SecondPageCursor}")
        {
            return $$"""{"accounts":[{{Summary()}}],"nextCursor":"{{SecondPageCursor}}"}""";
        }

        return shape == ListingShape.RepeatingItsCursor
            ? $$"""{"accounts":[{{SecondSummary()}}],"nextCursor":"{{SecondPageCursor}}"}"""
            : $$"""{"accounts":[{{SecondSummary()}}],"nextCursor":null}""";
    }

    private static string Summary() => string.Create(
        CultureInfo.InvariantCulture,
        $$"""{"id":"{{Account:D}}","version":{{AccountVersion}},"users":["{{User:D}}"],"emailAddress":"alex@example.test","displayName":"Work","organizationId":"{{Organization:D}}"}""");

    private static string SecondSummary() => string.Create(
        CultureInfo.InvariantCulture,
        $$"""{"id":"{{SecondAccount:D}}","version":1,"users":["{{User:D}}"],"emailAddress":"sam@example.test","displayName":"Home","organizationId":null}""");

    private static string Entry() => string.Create(
        CultureInfo.InvariantCulture,
        $$"""{"id":"{{Account:D}}","version":{{AccountVersion}},"users":["{{User:D}}"],"declaration":{{JsonSerializer.Serialize(Declaration)}},"organizationId":null}""");
}
