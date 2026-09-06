// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Chat;

namespace MailFathom.Application.Retrieval.AskMail;

/// <summary>Counts what one run has spent, refuses what would take it past what one question may cost, and reports what it has spent so far.</summary>
/// <remarks>
/// <para>
/// One instance serves one run, for the reason the run's retrieval does: a ceiling on a question is meaningless if two
/// questions share it. It is nevertheless internally synchronized, because a tool loop may answer several lookups of one
/// run at once, a streamed run reads its counts from a connection other than the one executing it, and both halves of a
/// check-then-take have to happen together.
/// </para>
/// <para>
/// The two kinds of refusal are deliberately unequal. Retrieval is trimmed to what the run may still send and the run
/// continues, because a question with some mail already retrieved is answerable and the model is told there is no more.
/// A provider call is refused outright, because a run with no allowance for another call has no answer to trim.
/// </para>
/// <para>
/// A token ceiling is checked before a call and can only be checked against what earlier calls reported, so the call
/// that crosses it is paid for. That is inherent rather than an oversight: what a call will cost is not knowable until
/// the provider has answered, and the alternative — estimating it — would refuse calls on a guess.
/// </para>
/// <para>
/// It is in this layer rather than beside the agent that first used it because a Discover run is orchestrated here and
/// spends at two ports it reaches the model through, so one ledger for the run has to be a thing this layer can hold.
/// What the run reports about what it spent is <see cref="Read" />, which is
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0022-what-an-ai-run-reports-about-cost-cancellation-and-the-model.md">ADR 0022</see>'s
/// unit: the run's own consumption against the ceilings that will stop it.
/// </para>
/// </remarks>
public sealed class MailAnsweringRunLedger
{
    private readonly Lock gate = new();
    private readonly HashSet<Guid> retrievedMessages = [];
    private int retrievedCharacters;
    private int providerCalls;
    private long tokens;

    /// <summary>Initializes a ledger for one run, with nothing spent.</summary>
    /// <param name="bounds">What this run may send, call, and consume.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="bounds" /> is <see langword="null" />.</exception>
    public MailAnsweringRunLedger(MailAnsweringRunBounds bounds)
    {
        ArgumentNullException.ThrowIfNull(bounds);

        this.Bounds = bounds;
    }

    /// <summary>Gets what this run may send, call, and consume, which is what its counts are read against.</summary>
    public MailAnsweringRunBounds Bounds { get; }

    /// <summary>Gets whether a lookup found mail this run's ceiling would not let it send.</summary>
    /// <remarks>The setter is written only from inside this type's lock, which is why it does not take one of its own; the getter does, because a reader outside the run holds nothing.</remarks>
    public bool RetrievalWasTruncated
    {
        get
        {
            lock (this.gate)
            {
                return field;
            }
        }

        private set;
    }

    /// <summary>Takes the passages of one lookup that still fit inside what this run may send.</summary>
    /// <param name="found">What the lookup found, in the order retrieval ranked them.</param>
    /// <returns>The leading passages that fit, which is every one of them until the ceiling is approached and none once it is reached.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="found" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// Whole passages rather than a cut across the last one. An extract already cut to the per-passage bound is a
    /// readable piece of a message, and cutting it again to fill the remaining allowance exactly would hand the model a
    /// sentence ending mid-word for the sake of a few hundred characters.
    /// </remarks>
    public IReadOnlyList<EmailKnowledgePassage> AdmitPassages(IReadOnlyList<EmailKnowledgePassage> found)
    {
        ArgumentNullException.ThrowIfNull(found);

        lock (this.gate)
        {
            List<EmailKnowledgePassage> admitted = [];

            foreach (var passage in found)
            {
                if (this.retrievedCharacters + passage.Text.Length > this.Bounds.MaximumRetrievedCharacters)
                {
                    // Stopping at the first passage that does not fit rather than searching the rest for a smaller one:
                    // retrieval handed these over in relevance order, and skipping ahead would silently prefer short
                    // messages to relevant ones the moment a run approached its ceiling.
                    this.RetrievalWasTruncated = true;

                    break;
                }

                this.retrievedCharacters += passage.Text.Length;
                this.retrievedMessages.Add(passage.StoredEmailId.Value);
                admitted.Add(passage);
            }

            return admitted;
        }
    }

    /// <summary>Takes this run's allowance for one more provider call.</summary>
    /// <exception cref="MailAnsweringBudgetExhaustedException">Thrown when the run has made every call it may make, or has consumed every token it may consume.</exception>
    public void RequireAllowanceForNextCall()
    {
        lock (this.gate)
        {
            if (this.providerCalls >= this.Bounds.MaximumProviderCalls || this.tokens >= this.Bounds.MaximumTokens)
            {
                throw MailAnsweringBudgetExhaustedException.RunSpent();
            }

            this.providerCalls++;
        }
    }

    /// <summary>Adds what one call consumed to this run's total.</summary>
    /// <param name="usage">The tokens the call sent and received.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="usage" /> is <see langword="null" />.</exception>
    public void RecordSpend(ChatTokenUsage usage)
    {
        ArgumentNullException.ThrowIfNull(usage);

        lock (this.gate)
        {
            this.tokens += usage.InputTokens + usage.OutputTokens;
        }
    }

    /// <summary>Reads what this run has consumed so far.</summary>
    /// <returns>The counts, taken together so no reader sees two of them from different moments.</returns>
    /// <remarks>
    /// Safe to call while the run is executing, which is what a streamed run publishes during a run rather than only at
    /// the end of one. It is a snapshot rather than a view: a run that spends more after it was read has not changed
    /// what was published, which is the honest reading of a figure that arrived at a moment.
    /// </remarks>
    public MailAnsweringRunSpend Read()
    {
        lock (this.gate)
        {
            return new MailAnsweringRunSpend(
                this.providerCalls,
                this.tokens,
                this.retrievedCharacters,
                this.retrievedMessages.Count);
        }
    }
}
