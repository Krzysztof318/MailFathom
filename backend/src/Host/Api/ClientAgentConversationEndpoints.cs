// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using System.Text.Json;
using MailFathom.Application.Access;
using MailFathom.Application.Agent.Answering;
using MailFathom.Application.Agent.Conversations;
using MailFathom.Application.Discovery.Presentation;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Domain.Access;
using MailFathom.Host.Configuration.Endpoints;
using MailFathom.Host.Security.Endpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace MailFathom.Host.Api;

/// <summary>Reads a person's conversations with the Agent, posts into them, steers and stops an answer, and answers what it proposed.</summary>
/// <remarks>
/// <para>
/// <strong>Every route answers from any replica, and none of them waits for an answer.</strong> A conversation is rows
/// every replica reads, so a question is written and answered with <c>202</c> at once, the composition writes into the
/// answer it opened, and a client follows it by reading the conversation from the place it already holds — the same
/// route for the first read and every later one.
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0035-delivering-a-running-ai-answer-from-a-persisted-run-by-cursor-signal-and-re-read.md">ADR 0035</see>
/// is that decision. Each write is announced as a <c>run.advanced</c> signal naming the conversation, the run, and the
/// place reached, and carrying nothing that was written; the routes are the whole contract, and a client with no hub
/// at all loses latency rather than any of the conversation.
/// </para>
/// <para>
/// <strong>Steering and stopping are two routes because they are two acts.</strong> An instruction posted into a run is
/// added to what that run is composing and taken from its next turn — nothing is interrupted, restarted, or thrown
/// away. Stopping ends the run where it stands, keeps everything it composed, and has the agent say it was stopped. Both
/// stay ordinary HTTP rather than hub methods, so they work precisely when the hub does not.
/// </para>
/// <para>
/// <strong>Every route resolves whose conversation it is from the signed-in account</strong>, and a conversation that
/// belongs to somebody else answers exactly as one that never existed. Nothing a request names can point a route at
/// another person's record.
/// </para>
/// <para>
/// A conversation is the person's own questions about their mail and the answers quoting it, so every route is
/// published under the grant that governs asking a question about mail, the reading ones included. None of what is
/// read or written reaches a log, a span, a problem detail, or a signal.
/// </para>
/// </remarks>
internal static class ClientAgentConversationEndpoints
{
    /// <summary>The route a person's conversations are listed at, relative to the client prefix.</summary>
    internal const string ConversationsRoute = "/agent/conversations";

    /// <summary>The route one conversation is read and deleted at, relative to the client prefix.</summary>
    internal const string ConversationRoute = "/agent/conversations/{conversationId:guid}";

    /// <summary>The route a question is asked at, relative to the client prefix.</summary>
    /// <remarks>Beneath the conversation it is asked in, which the client names — so the first question of a conversation is posted exactly as every later one is, and starts it.</remarks>
    internal const string MessagesRoute = "/agent/conversations/{conversationId:guid}/messages";

    /// <summary>The route a running answer is stopped at, relative to the client prefix.</summary>
    internal const string RunRoute = "/agent/conversations/{conversationId:guid}/runs/{runId:guid}";

    /// <summary>The route an instruction is added to a running answer at, relative to the client prefix.</summary>
    internal const string RunMessagesRoute = "/agent/conversations/{conversationId:guid}/runs/{runId:guid}/messages";

    /// <summary>The route a proposal is accepted or declined at, relative to the client prefix.</summary>
    /// <remarks>A proposal is named by the place it was written at, which is the one name that cannot come to mean a different offer.</remarks>
    internal const string ProposalRoute = "/agent/conversations/{conversationId:guid}/proposals/{proposedAt:long}";

    /// <summary>The greatest size a posted message may have on the wire.</summary>
    /// <remarks>Generous against the longest text a message may carry in any encoding, and small enough that a body is refused before it is read.</remarks>
    internal const int MaxMessageRequestBytes = 24 * 1024;

    /// <summary>The greatest size an answer to a proposal may have on the wire.</summary>
    internal const int MaxProposalAnswerRequestBytes = 1024;

    /// <summary>Maps the routes into the client group, so they inherit the group's requirement, its policy, and its limits.</summary>
    /// <param name="api">The client route group.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="api" /> is <see langword="null" />.</exception>
    internal static void MapClientAgentConversations(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        api.MapGet(ConversationsRoute, List)
            .RequirePermission(MailFathomPermission.MailAsk);

        api.MapGet(ConversationRoute, Read)
            .RequirePermission(MailFathomPermission.MailAsk);

        api.MapDelete(ConversationRoute, Delete)
            .RequirePermission(MailFathomPermission.MailAsk);

        api.MapPost(MessagesRoute, Ask)
            .WithMetadata(new RequestSizeLimitAttribute(MaxMessageRequestBytes))
            .RequirePermission(MailFathomPermission.MailAsk);

        api.MapPost(RunMessagesRoute, Steer)
            .WithMetadata(new RequestSizeLimitAttribute(MaxMessageRequestBytes))
            .RequirePermission(MailFathomPermission.MailAsk);

        api.MapDelete(RunRoute, Stop)
            .RequirePermission(MailFathomPermission.MailAsk);

        api.MapPut(ProposalRoute, AnswerProposal)
            .WithMetadata(new RequestSizeLimitAttribute(MaxProposalAnswerRequestBytes))
            .RequirePermission(MailFathomPermission.MailAsk);
    }

    /// <summary>Composes the address one conversation is read at.</summary>
    /// <param name="id">The conversation the address names.</param>
    /// <returns>The path, from the client prefix, that <c>202</c> points a client at.</returns>
    /// <remarks>Built out of the route the conversation is mapped at rather than written a second time, so the two cannot drift apart.</remarks>
    internal static string ReadAddressOf(AgentConversationId id) =>
        ClientEndpointOptions.RoutePrefix
        + ConversationRoute.Replace(
            "{conversationId:guid}",
            id.Value.ToString("D", CultureInfo.InvariantCulture),
            StringComparison.Ordinal);

    /// <summary>Lists the signed-in person's conversations, the one that moved most recently first.</summary>
    /// <param name="scopeResolver">Names the acting user, whose history this is.</param>
    /// <param name="store">Where conversations are held.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The history, bounded by <see cref="AgentConversationBounds.MaximumConversationsPerListing" />, or <c>403</c> for a caller whose grant does not carry <c>mailfathom.mail.ask</c>.</returns>
    internal static async Task<Ok<ClientAgentConversationListResponse>> List(
        [FromServices] MailboxScopeResolver scopeResolver,
        [FromServices] IAgentConversationStore store,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scopeResolver);
        ArgumentNullException.ThrowIfNull(store);

        var history = await store.ListAsync(
            scopeResolver.User,
            AgentConversationBounds.MaximumConversationsPerListing,
            cancellationToken);

        return TypedResults.Ok(new ClientAgentConversationListResponse(
        [
            .. history.Select(static line => new ClientAgentConversationSummary(
                line.Id.Value,
                line.Title,
                line.StartedAt,
                line.LastActivityAt)),
        ]));
    }

    /// <summary>Reads one conversation from wherever the caller left off, on whichever replica the request reached.</summary>
    /// <param name="conversationId">The conversation the caller is reading.</param>
    /// <param name="since">The last place the caller already holds, and absent to read from the beginning.</param>
    /// <param name="scopeResolver">Names the acting user, who is who may read it.</param>
    /// <param name="store">Where conversations are held.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The conversation's standing and everything written past that place, <c>404</c> where this person holds no such conversation, or <c>403</c> for a caller whose grant does not carry <c>mailfathom.mail.ask</c>.</returns>
    /// <remarks>
    /// <para>
    /// <strong>One route serves the first read and every later one.</strong> What comes back says whether an answer is
    /// still being composed, which is what tells a client to expect more, and whether more is already written than one
    /// read returns, which is a reader reading again from the last place it was given.
    /// </para>
    /// <para>
    /// A place below zero reads as the beginning, and so does one past what the conversation has reached: nobody holds
    /// such a value honestly, so it is a cursor belonging to something else, and replaying costs a few entries where
    /// honouring it would hand back a conversation missing its start.
    /// </para>
    /// <para>
    /// <strong>What comes back is the visible history and nothing else.</strong> A summary a compaction produced, a tool
    /// the agent called, what the tool answered, and what a call was charged are written into the same order to compose
    /// the model's input, and none of them is ever handed to a client — so every original message reads back exactly as
    /// it was written however many times the conversation was compacted. The places returned therefore skip where those
    /// entries stand; a page still holds as many entries as the bound allows wherever the history has them, and an empty
    /// page with nothing following is a reader that has caught up.
    /// </para>
    /// <para>
    /// Each entry is handed over as the record states it, under the same names and the same discriminator it is stored
    /// with, beside the place it was written at — so the order a client draws from is the database's rather than one it
    /// reconstructs.
    /// </para>
    /// </remarks>
    internal static async Task<Results<Ok<ClientAgentConversationReadResponse>, NotFound>> Read(
        [FromRoute] Guid conversationId,
        [FromQuery] long? since,
        [FromServices] MailboxScopeResolver scopeResolver,
        [FromServices] IAgentConversationStore store,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scopeResolver);
        ArgumentNullException.ThrowIfNull(store);

        if (conversationId == Guid.Empty)
        {
            return TypedResults.NotFound();
        }

        var read = await store.ReadAsync(
            AgentConversationId.Create(conversationId),
            scopeResolver.User,
            AgentConversationHistory.Visible,
            Math.Max(since ?? 0, 0),
            AgentConversationBounds.MaximumEntriesPerRead,
            cancellationToken);

        return read is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(new ClientAgentConversationReadResponse(
                read.Title,
                read.StartedAt,
                read.Composing,
                [.. read.Entries.Select(ClientAgentConversationEntry.For)],
                read.MoreFollows));
    }

    /// <summary>Deletes one conversation and everything said in it.</summary>
    /// <param name="conversationId">The conversation to delete.</param>
    /// <param name="scopeResolver">Names the acting user, who is who may delete it.</param>
    /// <param name="store">Where conversations are held.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns><c>204</c> where it was this person's and is gone, <c>404</c> where this person holds no such conversation, or <c>403</c> for a caller whose grant does not carry <c>mailfathom.mail.ask</c>.</returns>
    /// <remarks>
    /// An answer still being composed goes with it, and the run meets that as the first write the store refuses. A
    /// second request for the same conversation answers <c>404</c>, because by then there is none.
    /// </remarks>
    internal static async Task<Results<NoContent, NotFound>> Delete(
        [FromRoute] Guid conversationId,
        [FromServices] MailboxScopeResolver scopeResolver,
        [FromServices] IAgentConversationStore store,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scopeResolver);
        ArgumentNullException.ThrowIfNull(store);

        return conversationId != Guid.Empty
            && await store.TryDeleteAsync(AgentConversationId.Create(conversationId), scopeResolver.User, cancellationToken)
            ? TypedResults.NoContent()
            : TypedResults.NotFound();
    }

    /// <summary>Asks a question in a conversation, starting the conversation where it is new, and returns without waiting for the answer.</summary>
    /// <param name="conversationId">The conversation, which the client named.</param>
    /// <param name="request">The question, its identifier, and what it was asked about.</param>
    /// <param name="scopeResolver">Names the acting user, who is whose conversation it is.</param>
    /// <param name="controls">Writes the question, opens its answer, and announces both.</param>
    /// <param name="principals">Names the caller the transport admitted, whom the answer is composed under.</param>
    /// <param name="launcher">Composes the answer past this request.</param>
    /// <param name="timeProvider">Stamps the instant the question was asked at, which the answer is anchored to.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns><c>202</c> naming the message, the run answering it, and the place the conversation stands at; <c>400</c> naming what was wrong with the request; <c>404</c> where the conversation is somebody else's; <c>409</c> where an answer is still being composed or the conversation is full; or <c>403</c> for a caller whose grant does not carry <c>mailfathom.mail.ask</c>.</returns>
    /// <remarks>
    /// The message's identifier is the client's, so a post retried over a dropped connection answers with what the first
    /// one wrote — the same run and the same place — rather than writing the question twice, and the answer is started
    /// only by the post that wrote it.
    /// </remarks>
    internal static async Task<Results<Accepted<ClientAgentMessageResponse>, NotFound, ProblemHttpResult>> Ask(
        [FromRoute] Guid conversationId,
        [FromBody] ClientAgentMessageRequest? request,
        [FromServices] MailboxScopeResolver scopeResolver,
        [FromServices] AgentConversationControls controls,
        [FromServices] IAuthorizedPrincipalSource principals,
        [FromServices] AgentAnswerLauncher launcher,
        [FromServices] TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scopeResolver);
        ArgumentNullException.ThrowIfNull(controls);
        ArgumentNullException.ThrowIfNull(principals);
        ArgumentNullException.ThrowIfNull(launcher);
        ArgumentNullException.ThrowIfNull(timeProvider);

        AgentConversationId conversation;
        AgentMessageId message;
        PresentationText text;
        AgentMessageScope? scope;
        try
        {
            conversation = AgentConversationId.Create(conversationId);
            message = AgentMessageId.Create(request?.MessageId ?? Guid.Empty);
            text = PresentationText.Create(request?.Text);
            scope = request?.Scope is { } asked ? new AgentMessageScope(asked.Kind, asked.Subject) : null;
        }
        catch (ArgumentException)
        {
            return Refuse(
                "A question names the conversation and itself by non-empty identifiers, says something within the "
                + $"{PresentationText.MaxLength}-character bound, and names a declared scope with the object it narrows to.");
        }

        if (principals.Current is not { } caller)
        {
            return Refuse("A question is asked by a caller this deployment admitted.");
        }

        var askedAt = timeProvider.GetUtcNow();
        var posting = await controls.AskAsync(conversation, scopeResolver.User, message, text, scope, cancellationToken);

        if (posting is { Outcome: AgentMessagePostingOutcome.Written, Answer: { } answer })
        {
            _ = launcher.Start(
                new AgentQuestion(conversation, scopeResolver.User, text, scope, answer, posting.Reached, askedAt),
                caller);
        }

        return Answered(conversation, message, posting);
    }

    /// <summary>Adds an instruction to the answer being composed, which the run takes from its next turn without starting over.</summary>
    /// <param name="conversationId">The conversation holding the answer.</param>
    /// <param name="runId">The answer being steered.</param>
    /// <param name="request">The instruction and its identifier.</param>
    /// <param name="scopeResolver">Names the acting user, who is whose conversation it is.</param>
    /// <param name="controls">Writes the instruction and announces it.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns><c>202</c> naming the message, the run, and the place reached; <c>400</c> naming what was wrong with the request; <c>404</c> where this person holds no such conversation; <c>409</c> where that run is no longer being composed or the conversation is full; or <c>403</c> for a caller whose grant does not carry <c>mailfathom.mail.ask</c>.</returns>
    /// <remarks>
    /// Nothing already composed is touched and nothing is restarted. A run that ended before the instruction arrived has
    /// nothing left to steer, which is a <c>409</c> the person answers by asking afresh rather than an instruction left
    /// in the record for nobody.
    /// </remarks>
    internal static async Task<Results<Accepted<ClientAgentMessageResponse>, NotFound, ProblemHttpResult>> Steer(
        [FromRoute] Guid conversationId,
        [FromRoute] Guid runId,
        [FromBody] ClientAgentInstructionRequest? request,
        [FromServices] MailboxScopeResolver scopeResolver,
        [FromServices] AgentConversationControls controls,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scopeResolver);
        ArgumentNullException.ThrowIfNull(controls);

        AgentConversationId conversation;
        AgentMessageId run;
        AgentMessageId message;
        PresentationText text;
        try
        {
            conversation = AgentConversationId.Create(conversationId);
            run = AgentMessageId.Create(runId);
            message = AgentMessageId.Create(request?.MessageId ?? Guid.Empty);
            text = PresentationText.Create(request?.Text);
        }
        catch (ArgumentException)
        {
            return Refuse(
                "An instruction names the conversation, the run, and itself by non-empty identifiers, and says "
                + $"something within the {PresentationText.MaxLength}-character bound.");
        }

        var posting = await controls.SteerAsync(conversation, scopeResolver.User, run, message, text, cancellationToken);

        return Answered(conversation, message, posting);
    }

    /// <summary>Stops a running answer where it stands.</summary>
    /// <param name="conversationId">The conversation holding the answer.</param>
    /// <param name="runId">The answer to stop.</param>
    /// <param name="scopeResolver">Names the acting user, who is who may stop it.</param>
    /// <param name="controls">Ends the answer and has the agent say so.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns><c>204</c> where the run is stopped or had already ended, <c>404</c> where this person holds no such conversation, or <c>403</c> for a caller whose grant does not carry <c>mailfathom.mail.ask</c>.</returns>
    /// <remarks>
    /// <para>
    /// Nothing is rolled back: what the run composed stays in the conversation, and the agent writes a line saying it
    /// was stopped and asking where to pick it up. It is recorded rather than delivered to the process composing the
    /// answer, so it works from any replica — the run meets the stop as the first write the store refuses.
    /// </para>
    /// <para>
    /// A run that already ended is stopped successfully and nothing happens, because whoever asked could not have known
    /// it finished a moment earlier.
    /// </para>
    /// </remarks>
    internal static async Task<Results<NoContent, NotFound>> Stop(
        [FromRoute] Guid conversationId,
        [FromRoute] Guid runId,
        [FromServices] MailboxScopeResolver scopeResolver,
        [FromServices] AgentConversationControls controls,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scopeResolver);
        ArgumentNullException.ThrowIfNull(controls);

        if (conversationId == Guid.Empty || runId == Guid.Empty)
        {
            return TypedResults.NotFound();
        }

        var stopping = await controls.StopAsync(
            AgentConversationId.Create(conversationId),
            scopeResolver.User,
            AgentMessageId.Create(runId),
            cancellationToken);

        return stopping is AgentRunStopping.NoSuchConversation ? TypedResults.NotFound() : TypedResults.NoContent();
    }

    /// <summary>Accepts or declines one proposal the agent made.</summary>
    /// <param name="conversationId">The conversation holding the proposal.</param>
    /// <param name="proposedAt">The place the proposal was written at.</param>
    /// <param name="request">The decision.</param>
    /// <param name="scopeResolver">Names the acting user, whose proposal it has to be.</param>
    /// <param name="controls">Records a decline and announces it.</param>
    /// <param name="acceptance">Records an acceptance and carries out exactly the act that was proposed.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns><c>200</c> naming the place the last answer to the proposal was written at; <c>400</c> for a decision that is neither accepted nor declined; <c>409</c> where there is no proposal at that place this person can answer that way — none offered, not theirs, or already answered; or <c>403</c> for a caller whose grant does not carry <c>mailfathom.mail.ask</c>, or, on accepting, every grant the proposed act needs.</returns>
    /// <remarks>
    /// Accepting carries the act out, under the accepting caller's own grant, before this route answers: an act this
    /// deployment refuses — a recipient a governor refuses, an account no longer served — ends the proposal as failed,
    /// which the conversation shows. The refusals are one answer, so a proposal answered twice at once is answered once
    /// and carried out once, and a conversation belonging to somebody else reads as one holding no such proposal.
    /// </remarks>
    internal static async Task<Results<Ok<ClientAgentProposalAnswerResponse>, ProblemHttpResult>> AnswerProposal(
        [FromRoute] Guid conversationId,
        [FromRoute] long proposedAt,
        [FromBody] ClientAgentProposalAnswerRequest? request,
        [FromServices] MailboxScopeResolver scopeResolver,
        [FromServices] AgentConversationControls controls,
        [FromServices] AgentProposalAcceptance acceptance,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scopeResolver);
        ArgumentNullException.ThrowIfNull(controls);
        ArgumentNullException.ThrowIfNull(acceptance);

        AgentProposalState? decision = request?.Decision switch
        {
            ClientAgentProposalAnswerRequest.Accepted => AgentProposalState.Accepted,
            ClientAgentProposalAnswerRequest.Declined => AgentProposalState.Declined,
            _ => null,
        };

        if (decision is not { } decided || conversationId == Guid.Empty || proposedAt <= 0)
        {
            return TypedResults.Problem(
                $"A proposal is answered '{ClientAgentProposalAnswerRequest.Accepted}' or "
                + $"'{ClientAgentProposalAnswerRequest.Declined}', at the positive place it was written at.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var conversation = AgentConversationId.Create(conversationId);
        var answered = decided is AgentProposalState.Accepted
            ? await acceptance.AcceptAsync(conversation, scopeResolver.User, proposedAt, cancellationToken)
            : await controls.DeclineProposalAsync(conversation, scopeResolver.User, proposedAt, cancellationToken);

        return answered is { } place
            ? TypedResults.Ok(new ClientAgentProposalAnswerResponse(place))
            : TypedResults.Problem(
                "There is no proposal at that place this person can answer that way: none was offered there, or it has already been decided.",
                statusCode: StatusCodes.Status409Conflict);
    }

    /// <summary>Turns what became of a posted message into the answer a client acts on.</summary>
    private static Results<Accepted<ClientAgentMessageResponse>, NotFound, ProblemHttpResult> Answered(
        AgentConversationId conversation,
        AgentMessageId message,
        AgentMessagePosting posting) =>
        (posting.Stands, posting.Answer, posting.Outcome) switch
        {
            (true, { } run, _) => TypedResults.Accepted(
                ReadAddressOf(conversation),
                new ClientAgentMessageResponse(message.Value, run.Value, posting.Reached)),
            (true, null, _) => Conflict("That message identifier already names a different message in this conversation."),
            (_, _, AgentMessagePostingOutcome.NoSuchConversation) => TypedResults.NotFound(),
            (_, _, AgentMessagePostingOutcome.AnswerInProgress) => Conflict(
                "An answer is still being composed in this conversation. Steer it or stop it before asking again."),
            (_, _, AgentMessagePostingOutcome.NoAnswerInProgress) => Conflict(
                "That answer is no longer being composed, so there is nothing to steer. Ask a new question instead."),
            (_, _, AgentMessagePostingOutcome.TooManyConversations) => Conflict(
                "This person already holds as many conversations as one may. Delete one before starting another."),
            _ => Conflict("This conversation is full. Start another one."),
        };

    private static ProblemHttpResult Conflict(string stated) =>
        TypedResults.Problem(stated, statusCode: StatusCodes.Status409Conflict);

    /// <summary>States what a caller has to change, without echoing what they sent.</summary>
    /// <remarks>Without echoing it because a question is the most revealing value this surface carries, and a problem detail is the one part of a response that reaches a log by default.</remarks>
    private static Results<Accepted<ClientAgentMessageResponse>, NotFound, ProblemHttpResult> Refuse(string stated) =>
        TypedResults.Problem(stated, statusCode: StatusCodes.Status400BadRequest);
}

/// <summary>A question asked in a conversation.</summary>
/// <param name="MessageId">The question's identifier, which the client generates so a retried post writes nothing twice.</param>
/// <param name="Text">What the person asked.</param>
/// <param name="Scope">What the question was asked about, or <see langword="null" /> for one narrowing nothing further.</param>
/// <remarks>It names no user: whose conversation it is comes off the credential.</remarks>
internal sealed record ClientAgentMessageRequest(Guid? MessageId, string? Text, ClientAgentMessageScope? Scope);

/// <summary>What a question was asked about.</summary>
/// <param name="Kind">Which kind of thing — the mailbox, a thread, a calendar event, or a Discover run.</param>
/// <param name="Subject">The one it was asked about, and <see langword="null" /> for the whole mailbox.</param>
internal sealed record ClientAgentMessageScope(AgentScopeKind Kind, Guid? Subject);

/// <summary>An instruction added to a running answer.</summary>
/// <param name="MessageId">The instruction's identifier, which the client generates.</param>
/// <param name="Text">What the person added.</param>
internal sealed record ClientAgentInstructionRequest(Guid? MessageId, string? Text);

/// <summary>What a posted message became.</summary>
/// <param name="MessageId">The message, as the client named it.</param>
/// <param name="RunId">The run the message belongs to: the one a question opened, or the one an instruction steers.</param>
/// <param name="Sequence">The place the conversation stood at once the message was in it, which is a cursor the client may read from.</param>
internal sealed record ClientAgentMessageResponse(Guid MessageId, Guid RunId, long Sequence);

/// <summary>A decision on one proposal.</summary>
/// <param name="Decision"><c>accepted</c> or <c>declined</c>.</param>
internal sealed record ClientAgentProposalAnswerRequest(string? Decision)
{
    /// <summary>The word that accepts a proposal.</summary>
    internal const string Accepted = "accepted";

    /// <summary>The word that declines a proposal.</summary>
    internal const string Declined = "declined";
}

/// <summary>Where a decision on a proposal was written.</summary>
/// <param name="Sequence">The place the decision holds in the conversation.</param>
internal sealed record ClientAgentProposalAnswerResponse(long Sequence);

/// <summary>A person's conversation history.</summary>
/// <param name="Conversations">One line per conversation, the one that moved most recently first.</param>
internal sealed record ClientAgentConversationListResponse(IReadOnlyList<ClientAgentConversationSummary> Conversations);

/// <summary>One line of a person's conversation history.</summary>
/// <param name="Id">The conversation.</param>
/// <param name="Title">What it is called, and <see langword="null" /> until the agent names it.</param>
/// <param name="StartedAt">When it was started.</param>
/// <param name="LastActivityAt">When anything was last written into it.</param>
internal sealed record ClientAgentConversationSummary(
    Guid Id,
    string? Title,
    DateTimeOffset StartedAt,
    DateTimeOffset LastActivityAt);

/// <summary>One read of a conversation: where it stands, and everything written past the place the reader held.</summary>
/// <param name="Title">What the conversation is called, and <see langword="null" /> until the agent names it.</param>
/// <param name="StartedAt">When it was started.</param>
/// <param name="Composing">Whether an answer is still being composed, which tells a client following it to expect more.</param>
/// <param name="Entries">What was written past the cursor, in order, and empty where the reader was already caught up.</param>
/// <param name="MoreFollows">Whether more is already written than one read returns, which the reader fetches from the last place it was given.</param>
internal sealed record ClientAgentConversationReadResponse(
    string? Title,
    DateTimeOffset StartedAt,
    bool Composing,
    IReadOnlyList<ClientAgentConversationEntry> Entries,
    bool MoreFollows);

/// <summary>One entry of a conversation, beside the place it was written at.</summary>
/// <param name="Sequence">The place the entry holds, counted from one, which is the cursor a reader holds once it has this.</param>
/// <param name="Entry">The entry exactly as the record states it: its <c>entry</c> discriminator and its members under their stored names.</param>
internal sealed record ClientAgentConversationEntry(long Sequence, JsonElement Entry)
{
    /// <summary>Renders one stored entry for the wire.</summary>
    /// <param name="entry">The entry as it was read.</param>
    /// <returns>The entry beside its place.</returns>
    /// <remarks>Written by the entry's own serialization contract rather than the endpoint's, so the wire and the record name every member the same way and a reader needs one vocabulary for both.</remarks>
    internal static ClientAgentConversationEntry For(AgentConversationEntry entry) =>
        new(entry.Sequence, JsonSerializer.SerializeToElement(entry, AgentConversationEntryJsonContext.Default.AgentConversationEntry));
}
