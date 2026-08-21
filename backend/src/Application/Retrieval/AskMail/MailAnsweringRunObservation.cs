// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Domain.Answering.Audit;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Retrieval.AskMail;

/// <summary>Collects what one answering run did, so the run can be recorded whether or not it produced an answer.</summary>
/// <remarks>
/// <para>
/// It is created by the use case, handed to the answering port, and read back afterwards — including when the run threw.
/// That is what it exists for. A run that failed on its third provider call has already retrieved somebody's mail, and a
/// record built from the answer alone would say that run read nothing; the same is true of the span, whose counts and
/// outcome are most worth having exactly when the run did not end cleanly.
/// </para>
/// <para>
/// Passing it in is deliberately explicit rather than resolved from the container. A run's record must describe one run,
/// and an ambient value shared by whatever a scope happens to serve is one refactoring away from describing two.
/// </para>
/// <para>
/// It carries mail content in <see cref="MailAnsweringRetrievalReport.Passages" /> for the length of the call and no
/// longer, because the use case needs the passages to publish citations. Nothing derived from it is written to the
/// record, to a log, or to a span except identifiers and counts.
/// </para>
/// <para>
/// It is not thread-safe and needs not be: the composition and the retrieval are written by the run, in order, and the
/// outcome by the caller once the run has ended.
/// </para>
/// </remarks>
public sealed class MailAnsweringRunObservation
{
    /// <summary>Initializes the observation of one run that is about to begin.</summary>
    /// <param name="runId">What identifies the run in every entry it leaves behind.</param>
    /// <param name="scope">The accounts and folders the run may be answered from, already resolved.</param>
    /// <param name="startedAt">When the run began.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="scope" /> is <see langword="null" />.</exception>
    public MailAnsweringRunObservation(MailAnsweringRunId runId, MailboxScope scope, DateTimeOffset startedAt)
    {
        ArgumentNullException.ThrowIfNull(scope);

        this.RunId = runId;
        this.Scope = scope;
        this.StartedAt = startedAt;
        this.CompletedAt = startedAt;
    }

    /// <summary>Gets what identifies this run in every entry it leaves behind.</summary>
    public MailAnsweringRunId RunId { get; }

    /// <summary>Gets the accounts and folders the run was allowed to read.</summary>
    /// <remarks>
    /// The resolved scope rather than the requested one, so the accounts named here are the ones this deployment
    /// actually serves — which is what decides how many entries the run owes and to whom.
    /// </remarks>
    public MailboxScope Scope { get; }

    /// <summary>Gets when the run began.</summary>
    public DateTimeOffset StartedAt { get; }

    /// <summary>Gets when the run reached the ending it recorded, which is <see cref="StartedAt" /> until it has.</summary>
    public DateTimeOffset CompletedAt { get; private set; }

    /// <summary>Gets this deployment's own configured name for the chat endpoint the run was conducted through.</summary>
    public string ChatEndpointAlias { get; private set; } = string.Empty;

    /// <summary>Gets the version of the instruction the run was conducted under.</summary>
    public string InstructionsVersion { get; private set; } = string.Empty;

    /// <summary>Gets what the run's retrieval reached, which is <see cref="MailAnsweringRetrievalReport.Empty" /> until a run reports otherwise.</summary>
    public MailAnsweringRetrievalReport Retrieval { get; private set; } = MailAnsweringRetrievalReport.Empty;

    /// <summary>Gets how the run ended.</summary>
    /// <remarks>
    /// It reads <see cref="MailAnsweringRunOutcome.Failed" /> until an ending is recorded, which is the honest default:
    /// an observation nobody completed describes a run that ended in a way nothing named.
    /// </remarks>
    public MailAnsweringRunOutcome Outcome { get; private set; } = MailAnsweringRunOutcome.Failed;

    /// <summary>Gets the emails the published answer named as its sources, in the order the response lists them.</summary>
    public IReadOnlyList<StoredEmailId> CitedEmailIds { get; private set; } = [];

    /// <summary>Records what the run was composed against, before anything it could fail on.</summary>
    /// <param name="chatEndpointAlias">This deployment's own configured name for the endpoint conducting the run.</param>
    /// <param name="instructionsVersion">The version of the instruction the run is conducted under.</param>
    /// <exception cref="ArgumentException">Thrown when either argument is blank.</exception>
    /// <remarks>
    /// Called first rather than at the end, so a run that failed on its first call is still attributable to the profile
    /// and the policy that produced it — which is the pair somebody diagnosing it needs and the pair the record would
    /// otherwise have to leave blank.
    /// </remarks>
    public void RecordComposition(string chatEndpointAlias, string instructionsVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(chatEndpointAlias);
        ArgumentException.ThrowIfNullOrWhiteSpace(instructionsVersion);

        this.ChatEndpointAlias = chatEndpointAlias;
        this.InstructionsVersion = instructionsVersion;
    }

    /// <summary>Records what the run's retrieval reached in total.</summary>
    /// <param name="retrieval">The counts, the passages that reached the model, and how the retrieval degraded.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="retrieval" /> is <see langword="null" />.</exception>
    /// <remarks>Reported whether or not the run went on to produce an answer, which is why the run reports it as it ends rather than beside the answer.</remarks>
    public void RecordRetrieval(MailAnsweringRetrievalReport retrieval)
    {
        ArgumentNullException.ThrowIfNull(retrieval);

        this.Retrieval = retrieval;
    }

    /// <summary>Records how the run ended and which of the mail it read the answer went on to name.</summary>
    /// <param name="outcome">How the run ended.</param>
    /// <param name="citedEmailIds">The emails the published answer named, which is empty for every ending but an answered one.</param>
    /// <param name="completedAt">When the run reached that ending.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="citedEmailIds" /> is <see langword="null" />.</exception>
    public void RecordOutcome(
        MailAnsweringRunOutcome outcome,
        IReadOnlyList<StoredEmailId> citedEmailIds,
        DateTimeOffset completedAt)
    {
        ArgumentNullException.ThrowIfNull(citedEmailIds);

        this.Outcome = outcome;
        this.CitedEmailIds = citedEmailIds;
        this.CompletedAt = completedAt;
    }
}
