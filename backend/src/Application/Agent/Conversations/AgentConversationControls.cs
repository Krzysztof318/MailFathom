// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.Frozen;
using MailFathom.Application.Access;
using MailFathom.Application.Discovery.Presentation;
using MailFathom.Application.Signals;
using MailFathom.Domain.Access;

namespace MailFathom.Application.Agent.Conversations;

/// <summary>What a person does to a conversation: ask, steer the answer being composed, stop it, and answer a proposal.</summary>
/// <remarks>
/// <para>
/// Each of these writes into the conversation and then says so over the signal channel, naming the conversation, the
/// run where the write belongs to one, and the place reached — and nothing of what was written, which is read back over
/// the conversation's own route.
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0035-delivering-a-running-ai-answer-from-a-persisted-run-by-cursor-signal-and-re-read.md">ADR 0035</see>
/// is that shape. The signal is best effort: every write here is durable before it is announced, and a client that
/// never hears the announcement reads the same rows on its next read.
/// </para>
/// <para>
/// <strong>Every control works from any replica.</strong> Nothing here reaches the process composing an answer:
/// stopping one ends it in the record, and the run meets that as the first write the store refuses, wherever it is
/// executing. That is also what lets these stay ordinary requests that work while the hub does not — which is exactly
/// when spending most needs stopping.
/// </para>
/// <para>
/// Nothing here carries out a proposal. Accepting one records that a person said yes, which is what permits the
/// composition to carry it out; the act itself is never this class's.
/// </para>
/// </remarks>
public sealed class AgentConversationControls
{
    /// <summary>What the agent says when a person stopped its answer, in each language it writes for a person.</summary>
    /// <remarks>
    /// The design project's own words, and the agent's rather than the person's, so it is written in the language the
    /// deployment writes for this person. It says what arrived stays, because nothing is rolled back, and asks where to
    /// pick it up, because a stopped conversation is one to carry on in.
    /// </remarks>
    private static readonly FrozenDictionary<UserLanguage, PresentationText> StoppedNoteByLanguage =
        new Dictionary<UserLanguage, PresentationText>
        {
            [UserLanguage.English] = PresentationText.Create("Stopped. What arrived so far stays — tell me where to pick it up."),
            [UserLanguage.Polish] = PresentationText.Create("Zatrzymano. To, co już dotarło, zostaje — powiedz, od czego mam kontynuować."),
        }.ToFrozenDictionary();

    private readonly IAgentConversationStore store;
    private readonly ClientSignals signals;
    private readonly IUserLanguages languages;
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes the controls over the conversation store and the channel that announces each write.</summary>
    /// <param name="store">Where conversations are held.</param>
    /// <param name="signals">Announces that a conversation advanced.</param>
    /// <param name="languages">Resolves the language the agent's own words are written in for one person.</param>
    /// <param name="timeProvider">Stamps each write.</param>
    /// <exception cref="ArgumentNullException">Thrown when a collaborator is <see langword="null" />.</exception>
    public AgentConversationControls(
        IAgentConversationStore store,
        ClientSignals signals,
        IUserLanguages languages,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(signals);
        ArgumentNullException.ThrowIfNull(languages);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.store = store;
        this.signals = signals;
        this.languages = languages;
        this.timeProvider = timeProvider;
    }

    /// <summary>Asks a question, opening the answer to it and starting the conversation where it is new.</summary>
    /// <param name="conversation">The conversation, which the client named.</param>
    /// <param name="user">The person asking.</param>
    /// <param name="message">The question's identifier, which the client generated so a retried post writes nothing twice.</param>
    /// <param name="text">What the person asked.</param>
    /// <param name="scope">What the question was asked about, or <see langword="null" /> for a question narrowing nothing further.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>What became of the question, and the answer — the run — it opened.</returns>
    /// <remarks>
    /// It returns once the question is written and never waits for the answer: the composition writes into the answer
    /// this opens, and a client follows it by reading the conversation from the place this returns.
    /// </remarks>
    public async Task<AgentMessagePosting> AskAsync(
        AgentConversationId conversation,
        UserId user,
        AgentMessageId message,
        PresentationText text,
        AgentMessageScope? scope,
        CancellationToken cancellationToken)
    {
        var posting = await this.store.AskAsync(
            conversation,
            user,
            new AgentMessageWritten(message, AgentMessageAuthor.Person, text, scope),
            AgentMessageId.New(),
            this.timeProvider.GetUtcNow(),
            cancellationToken);

        if (posting.Outcome is AgentMessagePostingOutcome.Written)
        {
            this.Announce(user, conversation, posting.Answer, posting.Reached);
        }

        return posting;
    }

    /// <summary>Adds an instruction to the answer being composed, which takes it from its next turn without starting over.</summary>
    /// <param name="conversation">The conversation holding the answer.</param>
    /// <param name="user">The person steering.</param>
    /// <param name="run">The answer being steered.</param>
    /// <param name="message">The instruction's identifier, which the client generated.</param>
    /// <param name="text">What the person added.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>What became of the instruction.</returns>
    /// <remarks>
    /// Steering adds rather than interrupts: nothing already composed is touched, no second run starts, and the answer
    /// carries on from where it stands. It names no scope, because it is said inside the answer to a question that
    /// already stated one.
    /// </remarks>
    public async Task<AgentMessagePosting> SteerAsync(
        AgentConversationId conversation,
        UserId user,
        AgentMessageId run,
        AgentMessageId message,
        PresentationText text,
        CancellationToken cancellationToken)
    {
        var posting = await this.store.SteerAsync(
            conversation,
            user,
            run,
            new AgentMessageWritten(message, AgentMessageAuthor.Person, text, Scope: null),
            this.timeProvider.GetUtcNow(),
            cancellationToken);

        if (posting.Outcome is AgentMessagePostingOutcome.Written)
        {
            this.Announce(user, conversation, run, posting.Reached);
        }

        return posting;
    }

    /// <summary>Ends the answer being composed where it stands, and has the agent say so.</summary>
    /// <param name="conversation">The conversation holding the answer.</param>
    /// <param name="user">The person stopping it.</param>
    /// <param name="run">The answer to stop.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>Whether the answer was stopped here, was not running, or this person holds no such conversation.</returns>
    /// <remarks>
    /// <para>
    /// Nothing is rolled back: every block, source, and proposal already written stays, the answer ends as stopped, and
    /// the agent writes a line of its own saying so and asking where to pick it up. The line is a turn rather than a
    /// mark on the answer, because it is the agent speaking about the run rather than part of what the run composed.
    /// </para>
    /// <para>
    /// The ending is a condition on the conversation's row, so it lands on whichever replica this request reached and
    /// the run meets it as a refused write wherever it is executing.
    /// </para>
    /// </remarks>
    public async Task<AgentRunStopping> StopAsync(
        AgentConversationId conversation,
        UserId user,
        AgentMessageId run,
        CancellationToken cancellationToken)
    {
        var ended = await this.store.AppendAsync(
            conversation,
            user,
            new AgentAnswerEnded(run, AgentAnswerOutcome.Stopped),
            this.timeProvider.GetUtcNow(),
            cancellationToken);

        if (ended is null)
        {
            var held = await this.store.ReadAsync(conversation, user, afterSequence: 0, limit: 1, cancellationToken);

            return held is null ? AgentRunStopping.NoSuchConversation : AgentRunStopping.NotRunning;
        }

        var noted = await this.store.AppendAsync(
            conversation,
            user,
            new AgentMessageWritten(
                AgentMessageId.New(),
                AgentMessageAuthor.Agent,
                StoppedNoteByLanguage[this.languages.LanguageOf(user)],
                Scope: null),
            this.timeProvider.GetUtcNow(),
            cancellationToken);

        this.Announce(user, conversation, run, noted ?? ended.Value);

        return AgentRunStopping.Stopped;
    }

    /// <summary>Records that a person accepted or declined a proposal the agent made.</summary>
    /// <param name="conversation">The conversation holding the proposal.</param>
    /// <param name="user">The person answering.</param>
    /// <param name="proposedAt">The place the proposal was written at, which is what names it.</param>
    /// <param name="decision">Accepted or declined, which are the two answers a person gives.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The place the answer was written at, or <see langword="null" /> when it was not this person's to give from where the proposal stands.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="decision" /> is neither accepted nor declined.</exception>
    /// <remarks>
    /// Accepting executes nothing. It is what permits the composition to carry the proposal out, and what that then did
    /// reaches the record only as the state the proposal ends in — which is why failing is not a person's answer.
    /// </remarks>
    public async Task<long?> AnswerProposalAsync(
        AgentConversationId conversation,
        UserId user,
        long proposedAt,
        AgentProposalState decision,
        CancellationToken cancellationToken)
    {
        if (decision is not (AgentProposalState.Accepted or AgentProposalState.Declined))
        {
            throw new ArgumentException("A person accepts or declines a proposal; nothing else is theirs to record.", nameof(decision));
        }

        var answered = await this.store.TryResolveProposalAsync(
            conversation,
            user,
            proposedAt,
            decision,
            this.timeProvider.GetUtcNow(),
            cancellationToken);

        if (answered is { } place)
        {
            this.Announce(user, conversation, run: null, place);
        }

        return answered;
    }

    private void Announce(UserId user, AgentConversationId conversation, AgentMessageId? run, long reached) =>
        this.signals.Publish(ClientSignal.AgentConversationAdvanced(user, conversation, run, reached));
}
