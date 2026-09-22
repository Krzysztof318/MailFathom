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
    /// <param name="settlement">What settling an unanswered restore append answers with.</param>
    /// <returns>The deployment.</returns>
    internal static FakeHttpMessageHandler Answering(
        string? state = null,
        string? outcome = null,
        string? settlement = null)
    {
        var read = state ?? Held();
        var switched = outcome ?? Accepted();
        var settled = settlement ?? Settled();

        return new FakeHttpMessageHandler((request, _) => Task.FromResult(
            FakeAdminEndpoint.AnswerSession(request)
            ?? (request.RequestUri?.AbsolutePath switch
            {
                AdminEndpointRoutes.MailAccountCustodySwitchPath => FakeAdminEndpoint.Json(HttpStatusCode.OK, switched),
                AdminEndpointRoutes.MailAccountRestoreSettlementPath =>
                    FakeAdminEndpoint.Json(HttpStatusCode.OK, settled),
                AdminEndpointRoutes.MailAccountCustodyPath => FakeAdminEndpoint.Json(HttpStatusCode.OK, read),
                _ => FakeAdminEndpoint.Json(HttpStatusCode.NotFound, string.Empty),
            })));
    }

    /// <summary>Writes the body reading an account's custody answers with.</summary>
    /// <param name="requested">The custody last asked for.</param>
    /// <param name="phase">Which copy of the mailbox is the truth.</param>
    /// <param name="isSwitchPending">Whether the account is still moving towards what was asked for.</param>
    /// <returns>The response body.</returns>
    /// <remarks>
    /// The restore block is written as <c>null</c> rather than left out, because that is what a deployment sends for
    /// an account putting nothing back: the key is in the contract and its value is the absence.
    /// </remarks>
    internal static string Held(
        string requested = "HoldMailbox",
        string phase = "Held",
        bool isSwitchPending = false) =>
        $$$"""
           {"account":"work","requested":"{{{requested}}}","phase":"{{{phase}}}",
            "isSwitchPending":{{{(isSwitchPending ? "true" : "false")}}},
            "drain":{"awaitingDrain":4812,"heldBackAboveSizeLimit":3,
                     "heldBackAwaitingHeadroom":0,"awaitingSourceRemoval":7},
            "restore":null}
           """;

    /// <summary>Writes the body reading a restoring account's custody answers with.</summary>
    /// <param name="unansweredAppends">How many appends an operator has still to settle.</param>
    /// <returns>The response body.</returns>
    /// <remarks>
    /// A separate writer from <see cref="Held" /> because the two halves never both carry work: an account being put
    /// back onto its source has nothing left to drain, and a body carrying both would be one no deployment produces.
    /// </remarks>
    internal static string Restoring(int unansweredAppends = 1) =>
        $$$"""
           {"account":"work","requested":"MirrorSource","phase":"Restoring","isSwitchPending":true,
            "drain":{"awaitingDrain":0,"heldBackAboveSizeLimit":0,
                     "heldBackAwaitingHeadroom":0,"awaitingSourceRemoval":0},
            "restore":{"awaitingAppend":318,"awaitingStateWrite":12,
                       "unansweredAppends":{{{unansweredAppends}}},"awaitingConfirmation":2,
                       "unanswered":[{"record":"0199a7c4-6d21-7a55-9f1e-2c7d3b9a1f04",
                                      "folder":"archive","issuedAt":"2026-09-15T11:00:00+00:00"}]}}
           """;

    /// <summary>Writes the body settling an unanswered append answers with.</summary>
    /// <param name="wasSettled">Whether a record was still standing under the identity the operator named.</param>
    /// <returns>The response body.</returns>
    internal static string Settled(bool wasSettled = true) =>
        $$"""{"account":"work","wasSettled":{{(wasSettled ? "true" : "false")}}}""";

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
