// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Discovery.Presentation;
using MailFathom.Application.Discovery.Presentation.Citations;

namespace MailFathom.Application.Agent.Conversations;

/// <summary>One conversation between a person and this deployment's agent: what it is called, and every turn of it in order.</summary>
/// <remarks>
/// <para>
/// It is native to this deployment and stored beside everything else, in the same database as the mail it answers
/// about. It is per person, and it is durable: a conversation is not a session, and nothing here is cleared by a
/// restart, a reconnect, or the replica that was composing an answer going away.
/// </para>
/// <para>
/// <strong>This is a reading rather than the record.</strong> What the deployment holds is the ordered entries a
/// conversation was written as, one per thing that happened, and this is what those entries say when they are read
/// together: the turns in order, each with its blocks, its sources, and its offers. Composing it changes nothing and
/// decides nothing — the state of an offer is whatever the newest entry answering it said, and a reading taken twice
/// over the same entries is the same reading.
/// </para>
/// <para>
/// <strong>It is sensitive personal data throughout</strong>, by the same default every AI derivation in this product
/// carries: a question is somebody's own words, an answer quotes their correspondence, and a scope names what they
/// asked about. It reaches no log, no span attribute, no metric, and no failure message; it goes when the person does,
/// through the cascade from their record; and a conversation the person deletes takes everything it held with it.
/// </para>
/// </remarks>
public sealed record AgentConversation
{
    private AgentConversation(
        AgentConversationId id,
        string? title,
        DateTimeOffset startedAt,
        IReadOnlyList<AgentMessage> messages)
    {
        this.Id = id;
        this.Title = title;
        this.StartedAt = startedAt;
        this.Messages = messages;
    }

    /// <summary>Gets what addresses the conversation.</summary>
    public AgentConversationId Id { get; }

    /// <summary>Gets what the conversation is called, and <see langword="null" /> while nothing has named it.</summary>
    /// <remarks>Composed by the agent from the first question rather than typed by the person, which is why a conversation exists before it has one.</remarks>
    public string? Title { get; }

    /// <summary>Gets when the conversation was started, in UTC.</summary>
    public DateTimeOffset StartedAt { get; }

    /// <summary>Gets every turn, in the order they were written.</summary>
    public IReadOnlyList<AgentMessage> Messages { get; }

    /// <summary>Reads a conversation out of the entries it was written as.</summary>
    /// <param name="id">The conversation the entries belong to.</param>
    /// <param name="title">What the conversation is called, or <see langword="null" /> while nothing has named it.</param>
    /// <param name="startedAt">When the conversation was started, in UTC.</param>
    /// <param name="entries">The entries, from the conversation's beginning, in the order they were written.</param>
    /// <returns>What the entries say when they are read together.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="entries" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when an entry writes into a turn the entries before it never opened, answers an offer they never made, opens a turn they had already opened, or is of a kind this reading does not know.</exception>
    /// <remarks>
    /// <para>
    /// It takes the conversation from its beginning, which a bounded read of one also gives: a turn is opened before
    /// anything is written into it and an offer is made before it is answered, so any leading part of the order is a
    /// conversation this can read.
    /// </para>
    /// <para>
    /// What it refuses is an entry naming a turn or an offer that nothing earlier in the same entries opened — which
    /// is what a part taken from the middle of a conversation usually looks like, and is the whole of the guarantee.
    /// A part that happens to begin exactly where a turn does carries no such entry, so it composes as a conversation
    /// of its own with everything before it absent, and nothing here can tell that from a conversation that short.
    /// Reading from the beginning is therefore the caller's to arrange rather than this method's to verify; what a
    /// client watching an answer being composed reads is the entries themselves, which is the next paragraph.
    /// </para>
    /// <para>
    /// It is the reading the composition uses, where what a turn said matters more than the order the parts of it
    /// arrived in. A client watching an answer being composed reads the entries themselves, because what it is drawing
    /// is exactly their arrival.
    /// </para>
    /// <para>
    /// It is a reading of the visible history: an entry of the technical history alone — a tool call, a tool's answer, a
    /// charge, a summary — is passed over wherever it stands, so the same entries read the same way whether or not the
    /// record they came from held them.
    /// </para>
    /// </remarks>
    public static AgentConversation Compose(
        AgentConversationId id,
        string? title,
        DateTimeOffset startedAt,
        IReadOnlyList<AgentConversationEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var reading = new ReadingUnderConstruction();

        foreach (var entry in entries)
        {
            if (!reading.TryApply(entry, out var refusal))
            {
                throw new ArgumentException(refusal, nameof(entries));
            }
        }

        return new AgentConversation(id, title, startedAt, reading.Read());
    }

    /// <summary>The turns and offers a reading has placed so far, and the rule for placing one more.</summary>
    /// <remarks>
    /// A reading carries state from one entry to the next — which turns are open and which offers have been made — so
    /// it is written as a fold over the order rather than as a query of it. It reports a refusal instead of raising
    /// one, so the reading raises it against the argument it was actually given.
    /// </remarks>
    private sealed class ReadingUnderConstruction
    {
        private readonly List<TurnUnderConstruction> turns = [];
        private readonly Dictionary<AgentMessageId, TurnUnderConstruction> byMessage = [];
        private readonly Dictionary<long, OfferUnderConstruction> offers = [];

        private AgentMessageScope? inForce;

        internal bool TryApply(AgentConversationEntry entry, out string refusal)
        {
            refusal = string.Empty;

            switch (entry)
            {
                case AgentMessageWritten written:
                    return this.TryOpen(written.MessageId, written.Author, written, out refusal);

                case AgentAnswerStarted started:
                    return this.TryOpen(started.MessageId, AgentMessageAuthor.Agent, started, out refusal);

                case AgentStatusReported reported:
                    return this.TryWrite(reported, reported.MessageId, turn => turn.Status = reported.Status, out refusal);

                case AgentCitationDeclared declared:
                    return this.TryWrite(declared, declared.MessageId, turn => turn.Citations.Add(declared.Citation), out refusal);

                case AgentBlockComposed composed:
                    return this.TryWrite(composed, composed.MessageId, turn => turn.Blocks.Add(composed.Block), out refusal);

                case AgentActionProposed proposed:
                    return this.TryWrite(proposed, proposed.MessageId, turn => this.Offer(proposed, turn), out refusal);

                case AgentAnswerEnded ended:
                    return this.TryWrite(ended, ended.MessageId, turn => turn.Outcome = ended.Outcome, out refusal);

                case AgentProposalResolved resolved:
                    return this.TryAnswer(resolved, out refusal);

                // Written to compose a model input rather than to be read, so a reading of what was said passes over it.
                case { History: AgentConversationHistory.Technical }:
                    return true;

                default:
                    refusal = $"The entry at {entry.Sequence} is of a kind this reading does not know.";

                    return false;
            }
        }

        internal IReadOnlyList<AgentMessage> Read() => [.. this.turns.Select(turn => turn.Read())];

        private bool TryOpen(
            AgentMessageId message,
            AgentMessageAuthor author,
            AgentConversationEntry entry,
            out string refusal)
        {
            refusal = string.Empty;

            var opened = new TurnUnderConstruction(message, author, entry.Sequence);

            if (!this.byMessage.TryAdd(message, opened))
            {
                refusal = $"The entry at {entry.Sequence} opens a turn the conversation had already opened.";

                return false;
            }

            if (entry is AgentMessageWritten written)
            {
                opened.Text = written.Text;
                this.inForce = written.Scope ?? this.inForce;
            }

            // An answer is read under the scope its question stated, and so is a note the agent writes afterwards. The
            // record states it once, on the turn that asked, so carrying it forward is what makes every turn readable
            // on its own rather than only beside the turn before it.
            opened.Scope = this.inForce;

            this.turns.Add(opened);

            return true;
        }

        private bool TryWrite(
            AgentConversationEntry entry,
            AgentMessageId message,
            Action<TurnUnderConstruction> write,
            out string refusal)
        {
            refusal = string.Empty;

            if (!this.byMessage.TryGetValue(message, out var turn))
            {
                refusal = $"The entry at {entry.Sequence} writes into a turn the entries before it never opened.";

                return false;
            }

            write(turn);

            return true;
        }

        private void Offer(AgentActionProposed proposed, TurnUnderConstruction turn)
        {
            var offer = new OfferUnderConstruction(proposed.Sequence, proposed.Block);

            this.offers[proposed.Sequence] = offer;
            turn.Offers.Add(offer);
        }

        private bool TryAnswer(AgentProposalResolved resolved, out string refusal)
        {
            refusal = string.Empty;

            if (!this.offers.TryGetValue(resolved.ProposedAt, out var offer))
            {
                refusal = $"The entry at {resolved.Sequence} answers an offer the entries before it never made.";

                return false;
            }

            offer.State = resolved.State;

            return true;
        }
    }

    private sealed record TurnUnderConstruction(AgentMessageId Id, AgentMessageAuthor Author, long WrittenAt)
    {
        internal PresentationText? Text { get; set; }

        internal AgentMessageScope? Scope { get; set; }

        internal PresentationText? Status { get; set; }

        internal AgentAnswerOutcome? Outcome { get; set; }

        internal List<PresentationCitation> Citations { get; } = [];

        internal List<PresentationBlock> Blocks { get; } = [];

        internal List<OfferUnderConstruction> Offers { get; } = [];

        internal AgentMessage Read() => new(
            this.Id,
            this.Author,
            this.WrittenAt,
            this.Text,
            this.Scope,
            [.. this.Citations],
            [.. this.Blocks],
            [.. this.Offers.Select(offer => offer.Read())],
            this.Status,
            this.Outcome);
    }

    private sealed record OfferUnderConstruction(long ProposedAt, PresentationBlock Block)
    {
        internal AgentProposalState State { get; set; } = AgentProposalState.Pending;

        internal AgentProposedAction Read() => new(this.ProposedAt, this.Block, this.State);
    }
}
