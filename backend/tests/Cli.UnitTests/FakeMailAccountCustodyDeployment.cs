// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net;
using MailFathom.Cli.Administration;
using MailFathom.TestSupport;

namespace MailFathom.Cli.UnitTests;

/// <summary>A deployment answering the two custody routes, the one that reads and the one that changes.</summary>
/// <remarks>
/// One double answering both, because the commands are about which route each reaches: reading custody must never
/// reach the route that empties a mail server, and a double serving only the read would let a <c>show</c> that posted
/// pass unnoticed.
/// </remarks>
internal static class FakeMailAccountCustodyDeployment
{
    /// <summary>Builds a deployment answering both routes.</summary>
    /// <param name="state">What reading an account's custody answers with.</param>
    /// <param name="outcome">What asking for a custody answers with.</param>
    /// <returns>The deployment.</returns>
    internal static FakeHttpMessageHandler Answering(string? state = null, string? outcome = null)
    {
        var read = state ?? Held();
        var switched = outcome ?? Accepted();

        return new FakeHttpMessageHandler((request, _) => Task.FromResult(
            FakeAdminEndpoint.AnswerSession(request)
            ?? (request.RequestUri?.AbsolutePath switch
            {
                AdminEndpointRoutes.MailAccountCustodySwitchPath => FakeAdminEndpoint.Json(HttpStatusCode.OK, switched),
                AdminEndpointRoutes.MailAccountCustodyPath => FakeAdminEndpoint.Json(HttpStatusCode.OK, read),
                _ => FakeAdminEndpoint.Json(HttpStatusCode.NotFound, string.Empty),
            })));
    }

    /// <summary>Writes the body reading an account's custody answers with.</summary>
    /// <param name="requested">The custody last asked for.</param>
    /// <param name="phase">Which copy of the mailbox is the truth.</param>
    /// <param name="isSwitchPending">Whether the account is still moving towards what was asked for.</param>
    /// <returns>The response body.</returns>
    internal static string Held(
        string requested = "HoldMailbox",
        string phase = "Held",
        bool isSwitchPending = false) =>
        $$$"""
           {"account":"work","requested":"{{{requested}}}","phase":"{{{phase}}}",
            "isSwitchPending":{{{(isSwitchPending ? "true" : "false")}}},
            "drain":{"awaitingDrain":4812,"heldBackAboveSizeLimit":3,
                     "heldBackAwaitingHeadroom":0,"awaitingSourceRemoval":7}}
           """;

    /// <summary>Writes the body an accepted switch answers with.</summary>
    /// <param name="requested">The custody the account is now asked to have.</param>
    /// <param name="phase">Which copy of the mailbox is the truth at this moment.</param>
    /// <returns>The response body.</returns>
    internal static string Accepted(string requested = "HoldMailbox", string phase = "Mirrored") =>
        $$"""{"account":"work","wasAccepted":true,"requested":"{{requested}}","phase":"{{phase}}","refusals":[]}""";

    /// <summary>Writes the body a refused switch answers with, which carries one sentence per reason.</summary>
    /// <param name="refusals">The reasons, as the deployment words them.</param>
    /// <returns>The response body.</returns>
    /// <remarks>
    /// A refusal is answered as an outcome rather than as an HTTP failure, because every sentence in it is something
    /// an operator has to act on; an answer carrying only a status code would leave the command with nothing to print.
    /// </remarks>
    internal static string Refused(params string[] refusals) => string.Concat(
        """{"account":"work","wasAccepted":false,"requested":"HoldMailbox","phase":"Mirrored","refusals":[""",
        string.Join(',', refusals.Select(static refusal => $"\"{refusal}\"")),
        "]}");
}
