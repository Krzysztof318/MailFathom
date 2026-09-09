// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Discovery.Presentation.Citations;
using MailFathom.Application.Emails.ReplyDrafts;
using MailFathom.Application.Retrieval.AskMail;
using MailFathom.Domain.Access;
using MailFathom.Domain.Emails;
using MailFathom.Host.Security.Endpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace MailFathom.Host.Api;

/// <summary>Drafts the reply somebody is about to write, out of the conversation they are answering.</summary>
/// <remarks>
/// <para>
/// Two routes on one address, because a composer has two different questions and only one of them costs anything. The
/// read says whether this deployment drafts at all, which is what decides whether the composer may offer the
/// affordance before anybody presses it — a button that promises a draft over a deployment that cannot write one fails
/// a person at the one moment they trusted it. The write drafts one reply and costs a provider call.
/// </para>
/// <para>
/// <b>What comes back is a proposal rather than a message.</b> Nothing here sends, queues, or writes to a mail server,
/// and nothing is stored: the reply arrives as text in the composer, which its author edits, discards, or saves as a
/// draft of their own through the routes that already do that. Every irreversible act stays behind a person
/// confirming it, which is what makes a drafted reply a local artifact.
/// </para>
/// <para>
/// The claims are the half a sender acts on. Each one is something the reply asserts, and the ones carrying no source
/// are the ones the correspondence does not back — drawn as such before the message goes out rather than discovered
/// after it. Their sources are the presentation plan's own citation targets, so a reader follows one through the same
/// <c>POST /citations/resolution</c> a Discover answer's sources are followed through.
/// </para>
/// <para>
/// The answered message travels in a body rather than in a query string, and so does everything typed beside it. What
/// somebody is asking a reply to say is as revealing as the correspondence it answers, and a query string is the part
/// of a request that reaches an access log by default, on this deployment and on every proxy in front of it.
/// </para>
/// <para>
/// It is published under the grant that governs asking a question rather than the one that governs reading mail,
/// because that is what it does: a conversation and a sample of the account's own sent mail leave this deployment for
/// a chat provider, and the call is charged to the same allowance a question is.
/// </para>
/// </remarks>
internal static class ClientReplyDraftingEndpoint
{
    /// <summary>The route a reply is drafted at, relative to the client prefix.</summary>
    internal const string ReplyDraftingRoute = "/replies/drafting";

    /// <summary>The greatest request body a drafting reads before refusing it.</summary>
    /// <remarks>
    /// Generous against the two texts the request carries and the envelope around them, and small enough that a body
    /// over it is refused before it is read rather than after. The correspondence itself is not in it: a drafting names
    /// the message it answers and reads the exchange out of the store.
    /// </remarks>
    internal const int MaxDraftingRequestBytes = 16 * 1024;

    /// <summary>Maps the routes into the client group, so they inherit its requirement, its policy, and its limits.</summary>
    /// <param name="api">The client route group.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="api" /> is <see langword="null" />.</exception>
    internal static void MapClientReplyDrafting(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        api.MapGet(ReplyDraftingRoute, DraftsReplies)
            .RequirePermission(MailFathomPermission.MailAsk);

        // The MVC attribute is what a minimal API states this with, whatever its namespace suggests: it implements
        // IRequestSizeLimitMetadata, which the routing pipeline applies to the request body feature, so a body over
        // the bound is refused before anything reads it.
        api.MapPost(ReplyDraftingRoute, DraftReplyAsync)
            .WithMetadata(new RequestSizeLimitAttribute(MaxDraftingRequestBytes))
            .RequirePermission(MailFathomPermission.MailAsk);
    }

    /// <summary>Says whether this deployment drafts a reply at all.</summary>
    /// <param name="drafting">Drafts a reply, or <see langword="null" /> where this deployment declared no chat endpoint or declined this.</param>
    /// <returns><c>200</c> saying whether replies are drafted, or <c>403</c> for a caller whose grant does not carry <c>mailfathom.mail.ask</c>.</returns>
    /// <remarks>
    /// A capability rather than a probe: it resolves a registration and calls nothing, so a composer asking it once
    /// costs no provider call and no mail read. The two reasons a deployment does not draft — no endpoint, and an
    /// operator who turned this off — are one answer here on purpose, because what a client does about either is
    /// identical and naming which would publish a deployment's configuration to every signed-in browser.
    /// </remarks>
    internal static Ok<ClientReplyDraftingResponse> DraftsReplies(
        [FromServices] MailReplyDrafting? drafting) =>
        TypedResults.Ok(new ClientReplyDraftingResponse(drafting is not null));

    /// <summary>Drafts one reply, or reports what was wrong with the request.</summary>
    /// <param name="request">The message being answered, and what its author asked the reply to say.</param>
    /// <param name="drafting">Drafts the reply, or <see langword="null" /> where this deployment does not.</param>
    /// <param name="cancellationToken">Cancels the drafting when the client disconnects.</param>
    /// <returns><c>200</c> with the draft, <c>400</c> naming what was wrong with the request, <c>404</c> where this user has no such message, <c>429</c> where the deployment has spent what it allows a provider, or <c>403</c> for a caller whose grant does not carry <c>mailfathom.mail.ask</c>.</returns>
    /// <remarks>
    /// <para>
    /// A deployment that drafts nothing answers <c>200</c> saying so rather than <c>404</c>, because a person writing
    /// their own reply has not made a mistake and there is nothing for a client to repair. The same answer is what a
    /// provider that failed produces, and deliberately so: what follows either is the composer they were already
    /// looking at.
    /// </para>
    /// <para>
    /// A spend ceiling is the one failure that travels, because falling back there would leave somebody pressing a
    /// button the operator has already paid the last of the allowance for, told nothing.
    /// </para>
    /// </remarks>
    internal static async Task<Results<Ok<ClientReplyDraftResponse>, NotFound, ProblemHttpResult>> DraftReplyAsync(
        [FromBody] ClientReplyDraftRequest? request,
        [FromServices] MailReplyDrafting? drafting,
        CancellationToken cancellationToken)
    {
        if (request is null || request.AnsweredEmailId == Guid.Empty)
        {
            return Refuse("The request names no message to answer.");
        }

        if (request.Instruction is { Length: > ReplyDraftRequest.MaximumInstructionLength })
        {
            return Refuse(
                $"An instruction is at most {ReplyDraftRequest.MaximumInstructionLength} characters, "
                + "which is a sentence or two saying what the reply should say.");
        }

        if (request.Selection is { Length: > ReplyDraftRequest.MaximumSelectionLength })
        {
            return Refuse(
                $"A selection is at most {ReplyDraftRequest.MaximumSelectionLength} characters, "
                + "which is the part of the correspondence being answered rather than the whole of it.");
        }

        if (drafting is null)
        {
            return TypedResults.Ok(ClientReplyDraftResponse.NotDrafted);
        }

        try
        {
            var draft = await drafting.DraftAsync(
                new ReplyDraftRequest
                {
                    AnsweredEmailId = StoredEmailId.Create(request.AnsweredEmailId),
                    Selection = request.Selection,
                    Instruction = request.Instruction,
                },
                cancellationToken);

            return draft is null
                ? TypedResults.NotFound()
                : TypedResults.Ok(ClientReplyDraftResponse.For(draft));
        }
        catch (MailAnsweringBudgetExhaustedException refusal)
        {
            return TypedResults.Problem(refusal.Message, statusCode: StatusCodes.Status429TooManyRequests);
        }
    }

    /// <summary>States what a caller has to change, without echoing what they sent.</summary>
    /// <remarks>Without echoing it because what somebody asked a reply to say is as revealing as the correspondence, and a problem detail is the one part of a response that reaches a log by default.</remarks>
    private static ProblemHttpResult Refuse(string stated) =>
        TypedResults.Problem(stated, statusCode: StatusCodes.Status400BadRequest);
}

/// <summary>What this deployment does about drafting, which is what says whether a composer may offer it.</summary>
/// <param name="DraftsReplies">Whether a reply is drafted from the conversation it answers.</param>
/// <remarks>
/// One field and no reason beside it. A deployment that declared no chat endpoint and one whose operator turned this
/// off are the same answer to a composer — write it yourself — and naming which would publish a deployment's
/// configuration to every signed-in browser to answer a question nobody asked.
/// </remarks>
internal sealed record ClientReplyDraftingResponse(bool DraftsReplies);

/// <summary>One reply to draft: the message being answered, and what its author asked for.</summary>
/// <param name="AnsweredEmailId">The stored message the reply answers, as a message row published it.</param>
/// <param name="Selection">The part of the correspondence being answered, or <see langword="null" /> to answer the conversation as a whole.</param>
/// <param name="Instruction">What the reply should say, or <see langword="null" /> where the person asked for nothing in particular.</param>
/// <remarks>
/// The message is named and nothing about it is stated. The conversation, the people, the subject, and the account
/// whose manner the reply is written in are all read out of the stored copy that identifier resolves to, so a client
/// can state none of them and can state none of them wrongly.
/// </remarks>
internal sealed record ClientReplyDraftRequest(Guid AnsweredEmailId, string? Selection, string? Instruction);

/// <summary>The drafted reply, as the client endpoint serves it.</summary>
/// <param name="Drafted">Whether a reply was drafted at all, which is <see langword="false" /> where this deployment drafts none or the provider could not be reached.</param>
/// <param name="Body">The drafted reply as plain text, which is empty where none was drafted.</param>
/// <param name="Claims">What the reply asserts, in the order it was written, each with the messages backing it or with none.</param>
/// <param name="ProposedRecipients">The people the draft proposes to reach, every one of whom the conversation itself names.</param>
/// <remarks>
/// <para>
/// <c>drafted</c> being <see langword="false" /> is not a failure a client reports. It is the composer as it was, which
/// is what a deployment with no provider serves and what this one serves while its provider is unreachable.
/// </para>
/// <para>
/// All of it is derived from somebody's mail and is personal data of the same standing, so none of it reaches a log, a
/// span attribute, or a telemetry event.
/// </para>
/// </remarks>
internal sealed record ClientReplyDraftResponse(
    bool Drafted,
    string Body,
    IReadOnlyList<ClientReplyDraftClaimResponse> Claims,
    IReadOnlyList<ClientReplyDraftRecipientResponse> ProposedRecipients)
{
    /// <summary>The answer a drafting that produced nothing gives, which leaves the composer as it was.</summary>
    internal static ClientReplyDraftResponse NotDrafted { get; } = new(Drafted: false, string.Empty, [], []);

    /// <summary>Describes one draft for the wire.</summary>
    /// <param name="draft">The draft the use case answered with.</param>
    /// <returns>The response body.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="draft" /> is <see langword="null" />.</exception>
    internal static ClientReplyDraftResponse For(ReplyDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);

        return draft.WasWritten
            ? new ClientReplyDraftResponse(
                Drafted: true,
                draft.Body,
                [.. draft.Claims.Select(ClientReplyDraftClaimResponse.For)],
                [.. draft.ProposedRecipients.Select(ClientReplyDraftRecipientResponse.For)])
            : NotDrafted;
    }
}

/// <summary>One thing the drafted reply asserts.</summary>
/// <param name="Text">The assertion, as one sentence of the reply.</param>
/// <param name="Supported">Whether the correspondence backs it, which is <see langword="false" /> exactly when no source is named.</param>
/// <param name="Sources">The messages backing it, best first, spelled as the presentation plan spells a citation target.</param>
/// <remarks>
/// <c>supported</c> is published beside the sources rather than left to a client to derive from their count, because
/// what it means is a promise this deployment makes about the reply rather than an observation about an array: a
/// client drawing the mark from the array's length would be re-deciding, per client, what an unsupported claim is.
/// </remarks>
internal sealed record ClientReplyDraftClaimResponse(
    string Text,
    bool Supported,
    IReadOnlyList<ClientCitationRequest> Sources)
{
    /// <summary>Describes one claim for the wire.</summary>
    /// <param name="claim">The claim the draft carries.</param>
    /// <returns>The response body.</returns>
    internal static ClientReplyDraftClaimResponse For(ReplyDraftClaim claim) => new(
        claim.Text,
        claim.IsSupported,
        [
            .. claim.Sources.Select(static source => new ClientCitationRequest(
                EmailCitationTarget.Kind,
                source.Value,
                null,
                null)),
        ]);
}

/// <summary>One person the drafted reply proposes to reach.</summary>
/// <param name="Address">The address, which the conversation itself names.</param>
/// <param name="DisplayName">The name the conversation writes beside it, or <see langword="null" /> where it writes none.</param>
/// <remarks>
/// Shown to the person before anything is sent, and added to nothing on their behalf: the composer draws each of these
/// as a proposal they accept, change, or remove, which is what keeps a model from addressing mail.
/// </remarks>
internal sealed record ClientReplyDraftRecipientResponse(string Address, string? DisplayName)
{
    /// <summary>Describes one proposed recipient for the wire.</summary>
    /// <param name="address">The address the draft proposed.</param>
    /// <returns>The response body.</returns>
    internal static ClientReplyDraftRecipientResponse For(EmailAddress address) => new(
        address.Address,
        address.DisplayName);
}
