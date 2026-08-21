// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Chat;
using MailFathom.Application.Retrieval;
using MailFathom.Application.Retrieval.AskMail;

namespace MailFathom.AI.Orchestration;

/// <summary>Counts what one run has spent, and refuses what would take it past what one question may cost.</summary>
/// <remarks>
/// <para>
/// One instance serves one run, for the reason the run's retrieval does: a ceiling on a question is meaningless if two
/// questions share it. It is nevertheless internally synchronized, because the framework's tool loop may answer several
/// lookups of one run at once and both halves of a check-then-take have to happen together.
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
/// </remarks>
internal sealed class MailAnsweringRunLedger
{
    private readonly MailAnsweringRunBounds bounds;
    private readonly Lock gate = new();
    private int retrievedCharacters;
    private int providerCalls;
    private long tokens;

    /// <summary>Initializes a ledger for one run, with nothing spent.</summary>
    /// <param name="bounds">What this run may send, call, and consume.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="bounds" /> is <see langword="null" />.</exception>
    internal MailAnsweringRunLedger(MailAnsweringRunBounds bounds)
    {
        ArgumentNullException.ThrowIfNull(bounds);

        this.bounds = bounds;
    }

    /// <summary>Gets whether a lookup found mail this run's ceiling would not let it send.</summary>
    /// <remarks>The setter is written only from inside this type's lock, which is why it does not take one of its own; the getter does, because a reader outside the run holds nothing.</remarks>
    internal bool RetrievalWasTruncated
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
    internal IReadOnlyList<EmailKnowledgePassage> AdmitPassages(IReadOnlyList<EmailKnowledgePassage> found)
    {
        ArgumentNullException.ThrowIfNull(found);

        lock (this.gate)
        {
            List<EmailKnowledgePassage> admitted = [];

            foreach (var passage in found)
            {
                if (this.retrievedCharacters + passage.Text.Length > this.bounds.MaximumRetrievedCharacters)
                {
                    // Stopping at the first passage that does not fit rather than searching the rest for a smaller one:
                    // retrieval handed these over in relevance order, and skipping ahead would silently prefer short
                    // messages to relevant ones the moment a run approached its ceiling.
                    this.RetrievalWasTruncated = true;

                    break;
                }

                this.retrievedCharacters += passage.Text.Length;
                admitted.Add(passage);
            }

            return admitted;
        }
    }

    /// <summary>Takes this run's allowance for one more provider call.</summary>
    /// <exception cref="MailAnsweringBudgetExhaustedException">Thrown when the run has made every call it may make, or has consumed every token it may consume.</exception>
    internal void RequireAllowanceForNextCall()
    {
        lock (this.gate)
        {
            if (this.providerCalls >= this.bounds.MaximumProviderCalls || this.tokens >= this.bounds.MaximumTokens)
            {
                throw MailAnsweringBudgetExhaustedException.RunSpent();
            }

            this.providerCalls++;
        }
    }

    /// <summary>Adds what one call consumed to this run's total.</summary>
    /// <param name="usage">The tokens the call sent and received.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="usage" /> is <see langword="null" />.</exception>
    internal void RecordSpend(ChatTokenUsage usage)
    {
        ArgumentNullException.ThrowIfNull(usage);

        lock (this.gate)
        {
            this.tokens += usage.InputTokens + usage.OutputTokens;
        }
    }
}
