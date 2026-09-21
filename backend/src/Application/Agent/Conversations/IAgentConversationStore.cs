// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Discovery.Presentation;
using MailFathom.Domain.Access;

namespace MailFathom.Application.Agent.Conversations;

/// <summary>Where the deployment holds a person's conversations with its agent, and everything each of them was written as.</summary>
/// <remarks>
/// <para>
/// A conversation's contents are durable and reachable from every replica, which is what makes an answer survive the
/// replica composing it going away, a second screen the same person opened, and a rolling upgrade.
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0035-delivering-a-running-ai-answer-from-a-persisted-run-by-cursor-signal-and-re-read.md">ADR 0035</see>
/// is that decision, and this is the seam it is applied through for this surface.
/// </para>
/// <para>
/// <strong>What it holds is mail-derived and sensitive throughout.</strong> A question is somebody's own words, a block
/// quotes their correspondence, a citation names the message it was drawn from, and a scope names what they asked
/// about — so nothing read or written here reaches a log, a span, a metric, or a failure message. What a failure
/// carries is which table could not be reached.
/// </para>
/// <para>
/// <strong>Every statement is one round trip and takes its own decision.</strong> Appending assigns the place where
/// the row is written, so nothing outside the database decides what a conversation has reached; an entry belonging to
/// an answer is admitted only while that answer is the one being composed, which is how a run that was stopped writes
/// nothing further without anything having to reach the replica executing it; and answering a proposal is conditional
/// on where that proposal stands, so two people pressing at once produce one outcome rather than two.
/// </para>
/// <para>
/// Nothing here carries out a proposed action. An acceptance recorded through this store is what permits one to be
/// carried out; performing it belongs elsewhere, and what it did reaches this record only as the state the proposal
/// ends in.
/// </para>
/// </remarks>
public interface IAgentConversationStore
{
    /// <summary>Starts a conversation for one person, with nothing in it and no name yet.</summary>
    /// <param name="id">The identifier the conversation will be addressed by.</param>
    /// <param name="user">Whose conversation it is, which is who may read, add to, and delete it.</param>
    /// <param name="now">The instant it was started, in UTC.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns><see langword="true" /> when the conversation was started; <see langword="false" /> when one already exists under that identifier.</returns>
    Task<bool> TryStartAsync(
        AgentConversationId id,
        UserId user,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    /// <summary>Writes one entry into a conversation, giving it the next place in that conversation.</summary>
    /// <param name="id">The conversation the entry belongs to.</param>
    /// <param name="user">The person whose conversation it has to be.</param>
    /// <param name="entry">What happened, carrying neither a conversation nor a place of its own.</param>
    /// <param name="now">The instant the entry was written, in UTC.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The place the entry was written at, or <see langword="null" /> when nothing was written.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="entry" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when the entry answers a proposal, which is recorded through <see cref="TryResolveProposalAsync" /> because it is a person's act rather than a run's.</exception>
    /// <remarks>
    /// <para>
    /// The place starts at <c>1</c> and never skips, because the conversation's own counter is advanced inside the
    /// statement that writes the row rather than by anything counting outside the database. That also serializes two
    /// writers against each other, which a conversation genuinely has: a person may type while a run is composing.
    /// </para>
    /// <para>
    /// Nothing is written for a conversation that does not exist, for one that is not this person's, for one that is
    /// full, for an entry belonging to an answer that is not the one being composed, or for an answer opened while
    /// another is still being composed. Each of those is a statement about the conversation rather than a fault, so
    /// the caller stops rather than retrying.
    /// </para>
    /// <para>
    /// The person is checked here as everywhere else, and it is worth saying why the run's own writes are not the
    /// exception they look like. This is the path a person's own typed question takes as much as the path a composing
    /// run takes, so leaving the check to the caller would leave one write in this store — the only one somebody
    /// outside the deployment can cause — resting on a route remembering to do what every sibling method does itself.
    /// The run always knows whose conversation it is answering in, so nothing is bought by exempting it.
    /// </para>
    /// </remarks>
    Task<long?> AppendAsync(
        AgentConversationId id,
        UserId user,
        AgentConversationEntry entry,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    /// <summary>Writes a person's question and opens the answer to it, starting the conversation where it has not been started.</summary>
    /// <param name="id">The conversation, which the client named and which is started here when nothing stands under it yet.</param>
    /// <param name="user">The person asking, whose conversation it is or becomes.</param>
    /// <param name="question">The question, written by the person.</param>
    /// <param name="answer">The answer the question opens, which is the run composing it.</param>
    /// <param name="now">The instant the question was asked, in UTC.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>What became of the question, and the answer it opened.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="question" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="question" /> is not the person's own.</exception>
    /// <remarks>
    /// <para>
    /// <strong>The question and the opening of its answer are written together or not at all.</strong> A question left
    /// in the record with no answer opened behind it would read as an instruction to whatever answer came next, so the
    /// conversation's row is held for the whole of it and a question asked while an answer is still being composed
    /// writes nothing.
    /// </para>
    /// <para>
    /// <strong>A question already in the conversation is not written twice.</strong> Its identifier is the client's, so
    /// a second post under it is the first one retried, and what comes back is what the first one wrote — the same
    /// answer and the same place — rather than a refusal the client could not tell from a real one.
    /// </para>
    /// </remarks>
    Task<AgentMessagePosting> AskAsync(
        AgentConversationId id,
        UserId user,
        AgentMessageWritten question,
        AgentMessageId answer,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    /// <summary>Writes a person's further instruction into the answer being composed, which takes it from its next turn.</summary>
    /// <param name="id">The conversation holding the answer.</param>
    /// <param name="user">The person steering, whose conversation it has to be.</param>
    /// <param name="answer">The answer being steered, which has to be the one being composed.</param>
    /// <param name="instruction">What the person added, written by the person.</param>
    /// <param name="now">The instant it was written, in UTC.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>What became of the instruction.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="instruction" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="instruction" /> is not the person's own.</exception>
    /// <remarks>
    /// It adds and never interrupts: nothing already composed is touched, and the answer goes on from where it stands. An
    /// instruction for an answer that has ended writes nothing, because it would otherwise stand in the record as a
    /// question nobody answers. A repeated identifier is reported as <see cref="AgentMessagePostingOutcome.AlreadyWritten" />,
    /// exactly as a repeated question is.
    /// </remarks>
    Task<AgentMessagePosting> SteerAsync(
        AgentConversationId id,
        UserId user,
        AgentMessageId answer,
        AgentMessageWritten instruction,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    /// <summary>Records where a proposal this person was offered now stands.</summary>
    /// <param name="id">The conversation holding the proposal.</param>
    /// <param name="user">The person the caller was admitted for, and whose proposal it has to be.</param>
    /// <param name="proposedAt">The place the offer was written at, which is what names it.</param>
    /// <param name="state">Where it stands now, which is never <see cref="AgentProposalState.Pending" />.</param>
    /// <param name="now">The instant the answer was given, in UTC.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The place the answer was written at, or <see langword="null" /> when the move was refused.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="proposedAt" /> is not a written place, or when <paramref name="state" /> is not a declared member.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="state" /> is <see cref="AgentProposalState.Pending" />.</exception>
    /// <remarks>
    /// <para>
    /// The move is judged against where the proposal stands, in the statement that records it: a pending proposal may
    /// be accepted or declined, an accepted one may go on to have failed, and nothing else moves. So a proposal
    /// answered twice is answered once, which matters because an acceptance is what permits an act with a side effect.
    /// </para>
    /// <para>
    /// A refusal is one answer for every way the move was not this caller's to make — no such conversation, not theirs,
    /// nothing offered at that place, or the proposal already somewhere this move cannot follow.
    /// </para>
    /// </remarks>
    Task<long?> TryResolveProposalAsync(
        AgentConversationId id,
        UserId user,
        long proposedAt,
        AgentProposalState state,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    /// <summary>Reads a conversation this person holds, from a stated point.</summary>
    /// <param name="id">The conversation the caller is reading.</param>
    /// <param name="user">The person the caller was admitted for.</param>
    /// <param name="afterSequence">The last place the caller already holds, or <c>0</c> to read from the beginning.</param>
    /// <param name="limit">The greatest number of entries to return, at most <see cref="AgentConversationBounds.MaximumEntriesPerRead" />.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The conversation's standing and everything after that point, or <see langword="null" /> where this person has no such conversation.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="afterSequence" /> is negative, or when <paramref name="limit" /> is outside the bound.</exception>
    /// <remarks>
    /// A place this conversation has not reached reads as the beginning rather than as a point to wait at, which is the
    /// safe direction: no reader can hold one honestly, so what such a value means is a cursor belonging to some other
    /// conversation, and replaying costs a few entries where honouring it would hand back a conversation missing
    /// everything before the number. A conversation belonging to somebody else is reported as no such conversation,
    /// exactly as one that never existed is.
    /// </remarks>
    Task<AgentConversationReading?> ReadAsync(
        AgentConversationId id,
        UserId user,
        long afterSequence,
        int limit,
        CancellationToken cancellationToken);

    /// <summary>Lists a person's conversations, the one that moved most recently first.</summary>
    /// <param name="user">Whose history to read.</param>
    /// <param name="limit">The greatest number to return, at most <see cref="AgentConversationBounds.MaximumConversationsPerListing" />.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The lines of the history, and empty where this person has held none.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="limit" /> is outside the bound.</exception>
    Task<IReadOnlyList<AgentConversationSummary>> ListAsync(
        UserId user,
        int limit,
        CancellationToken cancellationToken);

    /// <summary>Names a conversation, which the agent does once it has read the first question.</summary>
    /// <param name="id">The conversation to name.</param>
    /// <param name="user">The person whose conversation it has to be.</param>
    /// <param name="title">What to call it, at most <see cref="AgentConversationBounds.MaximumTitleLength" /> characters.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns><see langword="true" /> when the conversation was named; <see langword="false" /> where this person has no such conversation.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="title" /> is the unspecified default or is longer than the bound.</exception>
    /// <remarks>
    /// It leaves the instant the history is ordered by alone, a name being a fact about the conversation rather than a
    /// turn of it, and it writes over whatever name the conversation held, so a later naming is the one that stands.
    /// The person is checked as in every other method here: the agent naming a conversation always knows whose it is
    /// answering in, so nothing is bought by making this the one write a caller could point anywhere.
    /// </remarks>
    Task<bool> TrySetTitleAsync(
        AgentConversationId id,
        UserId user,
        PresentationText title,
        CancellationToken cancellationToken);

    /// <summary>Removes a conversation this person holds, and everything it held.</summary>
    /// <param name="id">The conversation to remove.</param>
    /// <param name="user">The person the caller was admitted for.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns><see langword="true" /> when a conversation was removed; <see langword="false" /> where this person has no such conversation.</returns>
    /// <remarks>
    /// Every entry goes with the conversation, which is what makes the person's own erasure of one conversation a
    /// single statement — and the same cascade is why their whole record going takes all of them.
    /// </remarks>
    Task<bool> TryDeleteAsync(
        AgentConversationId id,
        UserId user,
        CancellationToken cancellationToken);
}
