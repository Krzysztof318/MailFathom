// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Agent.Conversations;
using MailFathom.Application.Discovery.Presentation;
using MailFathom.Application.Discovery.Presentation.Citations;
using MailFathom.Application.Localization;
using MailFathom.Application.Signals;
using MailFathom.Domain.Access;

namespace MailFathom.Application.Agent.Answering;

/// <summary>Writes one running answer into its conversation as it is composed, and tells the person's screens each time.</summary>
/// <remarks>
/// <para>
/// <strong>Every part is written where it is produced.</strong> A status line, a declared source, a block, and a
/// proposal each reach the conversation the moment the composition has them, so what a person sees survives the
/// connection that asked, a second screen, and the replica composing it going away — the condition
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0035-delivering-a-running-ai-answer-from-a-persisted-run-by-cursor-signal-and-re-read.md">ADR 0035</see>
/// rests on. Nothing here buffers.
/// </para>
/// <para>
/// <strong>A stop reaches the run as the first write the store refuses.</strong> The store admits an entry into an
/// answer only while that answer is the one being composed, so a stop recorded from any replica turns the next write
/// here into a refusal; <see cref="Stopping" /> is cancelled on it, and the composition sees its token end. Reading the
/// person's further instructions reads the conversation's standing too, so a stop is also met between two model calls
/// that wrote nothing.
/// </para>
/// </remarks>
public sealed class AgentAnswerJournal : IDisposable
{
    private readonly AgentConversationId conversation;
    private readonly UserId user;
    private readonly AgentMessageId answer;
    private readonly IAgentConversationStore store;
    private readonly ClientSignals signals;
    private readonly IUserLanguages languages;
    private readonly TimeProvider timeProvider;
    private readonly CancellationTokenSource stopping = new();
    private readonly HashSet<PresentationCitationId> declared = [];
    private long readUpTo;

    /// <summary>Initializes the journal of one answer the conversation has already opened.</summary>
    /// <param name="conversation">The conversation the answer belongs to.</param>
    /// <param name="user">Whose conversation it is.</param>
    /// <param name="answer">The answer being composed.</param>
    /// <param name="openedAt">The place the answer was opened at, which is where reading the person's further instructions begins.</param>
    /// <param name="store">Where the conversation is held.</param>
    /// <param name="signals">Announces that the conversation advanced.</param>
    /// <param name="languages">Resolves the language the status line is written in.</param>
    /// <param name="timeProvider">Stamps each write.</param>
    /// <exception cref="ArgumentNullException">Thrown when a collaborator is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="openedAt" /> is not a written place.</exception>
    public AgentAnswerJournal(
        AgentConversationId conversation,
        UserId user,
        AgentMessageId answer,
        long openedAt,
        IAgentConversationStore store,
        ClientSignals signals,
        IUserLanguages languages,
        TimeProvider timeProvider)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(openedAt);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(signals);
        ArgumentNullException.ThrowIfNull(languages);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.conversation = conversation;
        this.user = user;
        this.answer = answer;
        this.readUpTo = openedAt;
        this.store = store;
        this.signals = signals;
        this.languages = languages;
        this.timeProvider = timeProvider;
    }

    /// <summary>Gets a token cancelled once the answer is no longer the one being composed.</summary>
    /// <remarks>What cancels it is the conversation's own record — a write refused, or a read finding nothing composing — so it means the same thing whichever replica recorded the stop.</remarks>
    public CancellationToken Stopping => this.stopping.Token;

    /// <summary>Gets whether this journal wrote the answer's ending.</summary>
    public bool HasEnded { get; private set; }

    /// <summary>Replaces the status line with what the run is doing now.</summary>
    /// <param name="activity">What the run is doing.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns><see langword="true" /> when the line was written; <see langword="false" /> when the answer is no longer being composed.</returns>
    public Task<bool> ReportAsync(AgentActivity activity, CancellationToken cancellationToken) =>
        this.WriteAsync(
            new AgentStatusReported(
                this.answer,
                PresentationText.Create(this.languages.GetText(StatusOf(activity), this.user))),
            cancellationToken);

    /// <summary>Declares a source the answer's blocks may name, once however often it is declared.</summary>
    /// <param name="citation">The source, under the name blocks refer to it by.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns><see langword="true" /> when the source is declared, now or earlier; <see langword="false" /> when the answer is no longer being composed.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="citation" /> is <see langword="null" />.</exception>
    public async Task<bool> DeclareAsync(PresentationCitation citation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(citation);

        if (this.declared.Contains(citation.Id))
        {
            return true;
        }

        var written = await this.WriteAsync(new AgentCitationDeclared(this.answer, citation), cancellationToken);

        if (written)
        {
            this.declared.Add(citation.Id);
        }

        return written;
    }

    /// <summary>Writes a block of the answer, every source of which has to be declared already.</summary>
    /// <param name="block">The block, which the person only reads.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns><see langword="true" /> when the block was written; <see langword="false" /> when the answer is no longer being composed.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="block" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when the block names a source nothing declared, or is one a person acts on.</exception>
    public Task<bool> ComposeAsync(PresentationBlock block, CancellationToken cancellationToken)
    {
        this.RequireDeclared(block);

        return this.WriteAsync(new AgentBlockComposed(this.answer, block), cancellationToken);
    }

    /// <summary>Offers the person something to do, which nothing does until they accept it.</summary>
    /// <param name="block">The block presenting the offer.</param>
    /// <param name="act">What accepting it carries out.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns><see langword="true" /> when the offer was written; <see langword="false" /> when the answer is no longer being composed.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="block" /> or <paramref name="act" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when the block names a source nothing declared, or is one a person only reads.</exception>
    public Task<bool> ProposeAsync(PresentationBlock block, AgentProposedAct act, CancellationToken cancellationToken)
    {
        this.RequireDeclared(block);

        return this.WriteAsync(new AgentActionProposed(this.answer, block, act), cancellationToken);
    }

    /// <summary>Ends the answer, after which nothing further is written into it.</summary>
    /// <param name="outcome">How it ended.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns><see langword="true" /> when the ending was written; <see langword="false" /> when the answer had already ended.</returns>
    public async Task<bool> EndAsync(AgentAnswerOutcome outcome, CancellationToken cancellationToken)
    {
        var written = await this.WriteAsync(new AgentAnswerEnded(this.answer, outcome), cancellationToken);

        this.HasEnded |= written;

        return written;
    }

    /// <summary>Records a tool the model asked for, so the input its next call is composed from can be rebuilt from the record.</summary>
    /// <param name="callId">What the model named the call.</param>
    /// <param name="toolName">The tool it asked for.</param>
    /// <param name="arguments">The arguments, as the model sent them.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns><see langword="true" /> when the call was recorded; <see langword="false" /> when the answer is no longer being composed.</returns>
    public Task<bool> RecordToolCallAsync(string callId, string toolName, string arguments, CancellationToken cancellationToken) =>
        this.WriteAsync(new AgentToolCalled(this.answer, callId, toolName, arguments), cancellationToken);

    /// <summary>Records what a tool handed back to the model.</summary>
    /// <param name="callId">The call it answers.</param>
    /// <param name="result">What the model was sent.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns><see langword="true" /> when the answer was recorded; <see langword="false" /> when the answer is no longer being composed.</returns>
    public Task<bool> RecordToolResultAsync(string callId, string result, CancellationToken cancellationToken) =>
        this.WriteAsync(new AgentToolAnswered(this.answer, callId, result), cancellationToken);

    /// <summary>Records what the run's first call sent and was charged, which the next turn's budget is measured by.</summary>
    /// <param name="sentCharacters">How many characters of text the call sent.</param>
    /// <param name="inputTokens">How many input tokens the provider charged for them.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns><see langword="true" /> when the charge was recorded; <see langword="false" /> when the answer is no longer being composed.</returns>
    public Task<bool> RecordChargeAsync(long sentCharacters, long inputTokens, CancellationToken cancellationToken) =>
        this.WriteAsync(new AgentModelCharged(this.answer, sentCharacters, inputTokens), cancellationToken);

    /// <summary>Records a compaction taken for this answer's turn, which every later turn is composed from until the next one.</summary>
    /// <param name="compaction">What the compaction covers, the summary it produced, and the proposals carried beside it.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns><see langword="true" /> when the compaction was recorded; <see langword="false" /> when the answer is no longer being composed.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="compaction" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when the compaction was taken for another answer.</exception>
    public Task<bool> RecordCompactionAsync(AgentConversationCompacted compaction, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(compaction);

        if (compaction.MessageId != this.answer)
        {
            throw new ArgumentException("A compaction is recorded by the answer whose turn it was taken for.", nameof(compaction));
        }

        return this.WriteAsync(compaction, cancellationToken);
    }

    /// <summary>Reads what the person added to the answer since the last read, and meets a stop recorded meanwhile.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The person's instructions in the order they wrote them, and empty where they added nothing.</returns>
    /// <remarks>
    /// Steering adds rather than interrupts, so what this returns is taken from the run's next turn and nothing already
    /// composed is revisited. A conversation found composing nothing — stopped, or removed — cancels
    /// <see cref="Stopping" />, which is the stop reaching a run that has not written anything for a while.
    /// </remarks>
    public async Task<IReadOnlyList<PresentationText>> ReadInstructionsAsync(CancellationToken cancellationToken)
    {
        List<PresentationText> instructions = [];
        AgentConversationReading? reading;

        do
        {
            reading = await this.store.ReadAsync(
                this.conversation,
                this.user,
                AgentConversationHistory.Visible,
                this.readUpTo,
                AgentConversationBounds.MaximumEntriesPerRead,
                cancellationToken);

            if (reading is null)
            {
                break;
            }

            instructions.AddRange(reading.Entries
                .OfType<AgentMessageWritten>()
                .Where(static message => message.Author is AgentMessageAuthor.Person)
                .Select(static message => message.Text));

            this.readUpTo = reading.Entries.Count is 0 ? this.readUpTo : reading.Entries[^1].Sequence;
        }
        while (reading.MoreFollows);

        if (reading is not { Composing: true })
        {
            await this.stopping.CancelAsync();
        }

        return instructions;
    }

    /// <inheritdoc />
    public void Dispose() => this.stopping.Dispose();

    private static ApplicationText StatusOf(AgentActivity activity) => activity switch
    {
        AgentActivity.ReadingQuestion => ApplicationText.AgentStatusReadingQuestion,
        AgentActivity.SearchingMail => ApplicationText.AgentStatusSearchingMail,
        AgentActivity.ReadingThread => ApplicationText.AgentStatusReadingThread,
        AgentActivity.ReadingCalendar => ApplicationText.AgentStatusReadingCalendar,
        AgentActivity.ReadingTasks => ApplicationText.AgentStatusReadingTasks,
        AgentActivity.PreparingProposal => ApplicationText.AgentStatusPreparingProposal,
        AgentActivity.ComposingAnswer => ApplicationText.AgentStatusComposingAnswer,
        _ => throw new ArgumentOutOfRangeException(nameof(activity), activity, "A run reports a declared activity."),
    };

    private void RequireDeclared(PresentationBlock block)
    {
        ArgumentNullException.ThrowIfNull(block);

        if (block.ReferencedCitations.Any(citation => !this.declared.Contains(citation)))
        {
            throw new ArgumentException("A block names only sources the answer has already declared.", nameof(block));
        }
    }

    private async Task<bool> WriteAsync(AgentConversationEntry entry, CancellationToken cancellationToken)
    {
        var place = await this.store.AppendAsync(
            this.conversation,
            this.user,
            entry,
            this.timeProvider.GetUtcNow(),
            cancellationToken);

        if (place is not { } reached)
        {
            await this.stopping.CancelAsync();

            return false;
        }

        // A technical entry is one no screen draws, so telling a screen the conversation moved would only make it read
        // again and find nothing new.
        if (entry.History is AgentConversationHistory.Visible)
        {
            this.signals.Publish(ClientSignal.AgentConversationAdvanced(this.user, this.conversation, this.answer, reached));
        }

        return true;
    }
}
