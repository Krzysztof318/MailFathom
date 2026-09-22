// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Agent.Conversations;
using MailFathom.Application.Chat;
using MailFathom.Application.Discovery.Presentation;
using MailFathom.Application.Retrieval.AskMail;
using MailFathom.Application.Signals;

namespace MailFathom.Application.Agent.Answering;

/// <summary>Answers one question a conversation recorded, writing the answer into it as it is composed and ending it however the run went.</summary>
/// <remarks>
/// <para>
/// <strong>The run outlives the connection that asked, and every connection.</strong> Nothing here reads a request: it
/// is handed a question the conversation already holds, composes with no client attached, and stops only on a stop the
/// person recorded, on a ceiling it reaches, or on the process going away. Whatever ended it, the answer ends in the
/// record, so a conversation read later says how.
/// </para>
/// <para>
/// <strong>It spends like every other answer.</strong> The run is admitted against the deployment's period before any
/// provider is reached, and every call it makes is counted by the run's own ceiling and the provider's bulkhead inside
/// the composition — the same three every on-request derivation here passes through, rather than an unmetered fourth.
/// </para>
/// <para>
/// <strong>A turn sends at most what the deployment's budget allows.</strong> A conversation that has outgrown it is
/// compacted first: what came earlier is summarised, the summary is recorded beside everything it covers, and the turn
/// is composed from the newest summary plus everything written after it verbatim. A summary is never shown, never
/// announced, and never a message, and a summariser that fails does not fail the turn — the turn is composed from as
/// much recent history as fits, nothing is recorded, and the failure is the operator's to read rather than the person's.
/// </para>
/// </remarks>
public sealed class AgentAnswering
{
    /// <summary>The longest a run may compose before it is ended as failed.</summary>
    /// <remarks>
    /// Longer than a Discover run's, because an answer here may read a thread, a calendar, and a task list before it
    /// writes, and shorter than anything a person would still be waiting for. It is a ceiling on one run, not a policy
    /// an operator sets.
    /// </remarks>
    public static readonly TimeSpan MaximumDuration = TimeSpan.FromMinutes(10);

    private readonly IAgentConversationStore store;
    private readonly IAgentAnswerComposer? composer;
    private readonly IAgentConversationSummarizer? summarizer;
    private readonly AgentContextBudget contextBudget;
    private readonly IMailAnsweringSpendLedger spendLedger;
    private readonly ClientSignals signals;
    private readonly IUserLanguages languages;
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes the use case.</summary>
    /// <param name="store">Where the conversation is held.</param>
    /// <param name="composer">Composes the answer, or <see langword="null" /> on a deployment that declared no chat endpoint, where every run ends as failed before anything is spent.</param>
    /// <param name="summarizer">Summarises a conversation that outgrew the budget, or <see langword="null" /> where no chat endpoint was declared.</param>
    /// <param name="contextBudget">What one turn may send before its earlier part is compacted.</param>
    /// <param name="spendLedger">Admits the run against the deployment's period.</param>
    /// <param name="signals">Announces each write.</param>
    /// <param name="languages">Resolves the person's language.</param>
    /// <param name="timeProvider">Stamps each write and measures the run's ceiling on time.</param>
    /// <exception cref="ArgumentNullException">Thrown when a required collaborator is <see langword="null" />.</exception>
    public AgentAnswering(
        IAgentConversationStore store,
        IAgentAnswerComposer? composer,
        IAgentConversationSummarizer? summarizer,
        AgentContextBudget contextBudget,
        IMailAnsweringSpendLedger spendLedger,
        ClientSignals signals,
        IUserLanguages languages,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(contextBudget);
        ArgumentNullException.ThrowIfNull(spendLedger);
        ArgumentNullException.ThrowIfNull(signals);
        ArgumentNullException.ThrowIfNull(languages);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.store = store;
        this.composer = composer;
        this.summarizer = summarizer;
        this.contextBudget = contextBudget;
        this.spendLedger = spendLedger;
        this.signals = signals;
        this.languages = languages;
        this.timeProvider = timeProvider;
    }

    /// <summary>Composes the answer to one question and leaves it ended.</summary>
    /// <param name="question">The question the conversation recorded, and the answer it opened.</param>
    /// <param name="cancellationToken">Ends the run when the process is stopping.</param>
    /// <returns>A task that completes once the answer has ended.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="question" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// A failure this use case has a name for ends the answer as failed and is not raised; one it does not is raised
    /// after the answer has been ended, so the caller can report it without leaving the conversation composing forever.
    /// A stop the person recorded writes nothing here, the store having already ended the answer with the stop.
    /// </remarks>
    public async Task RunAsync(AgentQuestion question, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(question);

        using var journal = new AgentAnswerJournal(
            question.Conversation,
            question.User,
            question.Answer,
            question.OpenedAt,
            this.store,
            this.signals,
            this.languages,
            this.timeProvider);

        await this.ComposeUntilEndedAsync(question, journal, cancellationToken);
    }

    private static bool IsNamed(Exception failure, CancellationToken run) => failure switch
    {
        MailAnsweringBudgetExhaustedException or MailAnsweringUnavailableException or ChatGenerationFailedException => true,
        OperationCanceledException => run.IsCancellationRequested,
        _ => false,
    };

    /// <summary>Names a conversation after the question that started it, which is the person's own words.</summary>
    private static PresentationText TitleOf(PresentationText question)
    {
        var text = question.Value.ReplaceLineEndings(" ").Trim();

        return PresentationText.Create(
            text.Length <= AgentConversationBounds.MaximumTitleLength
                ? text
                : string.Concat(text.AsSpan(0, AgentConversationBounds.MaximumTitleLength - 1), "…"));
    }

    private async Task ComposeUntilEndedAsync(AgentQuestion question, AgentAnswerJournal journal, CancellationToken cancellationToken)
    {
        using var ceiling = new CancellationTokenSource(MaximumDuration, this.timeProvider);
        using var run = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            ceiling.Token,
            journal.Stopping);

        try
        {
            await this.ComposeAsync(question, journal, run.Token);
        }
        catch (OperationCanceledException) when (journal.Stopping.IsCancellationRequested)
        {
            // The store refused a write: the person stopped the answer, which wrote its ending with the stop, or the
            // conversation filled, which the ending below still has a kept place for.
        }
        catch (Exception failure) when (IsNamed(failure, run.Token))
        {
            await journal.EndAsync(AgentAnswerOutcome.Failed, CancellationToken.None);
        }
        finally
        {
            // Attempted whatever ended the run, because the store tells the two refusals apart where this cannot: an
            // answer already ended by a stop refuses a second ending, and one refused only because the conversation
            // filled takes one of the places kept for exactly this.
            if (!journal.HasEnded)
            {
                await journal.EndAsync(AgentAnswerOutcome.Failed, CancellationToken.None);
            }
        }
    }

    private async Task ComposeAsync(AgentQuestion question, AgentAnswerJournal journal, CancellationToken cancellationToken)
    {
        if (!await journal.ReportAsync(AgentActivity.ReadingQuestion, cancellationToken))
        {
            return;
        }

        if (this.composer is null)
        {
            throw MailAnsweringUnavailableException.NotServed();
        }

        var earlier = await this.ReadEarlierAsync(question, cancellationToken);

        if (!await this.spendLedger.TryAdmitRunAsync(cancellationToken))
        {
            throw MailAnsweringBudgetExhaustedException.PeriodSpent();
        }

        if (earlier.Title is null)
        {
            await this.store.TrySetTitleAsync(question.Conversation, question.User, TitleOf(question.Text), cancellationToken);
        }

        var history = await this.ComposeHistoryAsync(question, earlier.Context, journal, cancellationToken);

        // The scope the conversation stands under rides on the question's own turn rather than in the history, so the
        // context it was opened from is stated verbatim on every turn and is never left for a summary to paraphrase.
        var brief = new AgentAnswerBrief(
            question with { Scope = question.Scope ?? earlier.Context.ScopeInForce },
            this.languages.LanguageOf(question.User),
            history);

        await this.composer.ComposeAsync(brief, journal, cancellationToken);
        await journal.EndAsync(AgentAnswerOutcome.Completed, cancellationToken);
    }

    /// <summary>Composes the history the turn sends, compacting the conversation first where it has outgrown the budget.</summary>
    private async Task<IReadOnlyList<AgentHistoryTurn>> ComposeHistoryAsync(
        AgentQuestion question,
        AgentConversationContext context,
        AgentAnswerJournal journal,
        CancellationToken cancellationToken)
    {
        var budget = this.contextBudget.Tokens;
        var history = context.Compose();

        if (context.EstimateTokens(history, question.Text.Value) <= budget)
        {
            return history;
        }

        var plan = context.PlanCompaction(budget);
        var summary = plan is null || this.summarizer is null
            ? null
            : await this.summarizer.SummarizeAsync(plan.PreviousSummary, plan.Turns, cancellationToken);

        if (plan is null || summary is null)
        {
            return context.ComposeWithin(budget, question.Text.Value);
        }

        var compaction = new AgentConversationCompacted(question.Answer, plan.Through, summary, plan.Carried);

        if (!await journal.RecordCompactionAsync(compaction, cancellationToken))
        {
            journal.Stopping.ThrowIfCancellationRequested();
        }

        return context.ComposeFrom(compaction);
    }

    /// <summary>Reads the technical history up to the question, which is what the turn is composed from.</summary>
    /// <remarks>
    /// ponytail: every entry before the question is read, tool traffic included, though composing needs only the
    /// visible turns after the newest summary, that summary, and the newest charge. A read that starts at the newest
    /// compaction and passes over tool calls and results is the upgrade once a long conversation's turn is measured
    /// spending its time here.
    /// </remarks>
    private async Task<(string? Title, AgentConversationContext Context)> ReadEarlierAsync(
        AgentQuestion question,
        CancellationToken cancellationToken)
    {
        List<AgentConversationEntry> entries = [];
        string? title = null;
        long after = 0;
        var asked = question.OpenedAt - 1;
        AgentConversationReading? reading;

        do
        {
            reading = await this.store.ReadAsync(
                question.Conversation,
                question.User,
                AgentConversationHistory.Technical,
                after,
                AgentConversationBounds.MaximumEntriesPerRead,
                cancellationToken);

            if (reading is null)
            {
                break;
            }

            title = reading.Title;
            entries.AddRange(reading.Entries.Where(entry => entry.Sequence < asked));
            after = reading.Entries.Count is 0 ? after : reading.Entries[^1].Sequence;
        }
        while (reading.MoreFollows && after < asked);

        return (title, AgentConversationContext.Read(entries));
    }
}
