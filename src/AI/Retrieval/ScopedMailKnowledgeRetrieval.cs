// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.Orchestration;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Retrieval;
using MailFathom.Application.Retrieval.AskMail;
using MailFathom.Domain.Answering.Audit;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Logging;

namespace MailFathom.AI.Retrieval;

/// <summary>The mail one run may reach, and the record of what it reached.</summary>
/// <remarks>
/// <para>
/// This is where the scope stops being negotiable. The framework is handed a search that takes a query and nothing else,
/// because the accounts and folders were bound into this object when the run was composed: a model can write any query it
/// likes and every one of them is answered from the same scope. Nothing in an instruction, a retrieved message, or a tool
/// argument reaches the value, which is what makes the boundary structural rather than a rule the prompt asks for.
/// </para>
/// <para>
/// One instance serves one run. It records the passages it handed over so the answer can carry them, and that record is
/// per run for the same reason the scope is: one caller's retrieved mail must never appear beside another's.
/// </para>
/// <para>
/// It is also where the run's ceiling on retrieved mail is applied, and applying it here is what makes it a bound on
/// what leaves the process rather than a bound on what a lookup returns. A model can ask for mail as many times as the
/// tool loop allows; each answer is trimmed to what this run may still send, and once nothing may be sent the envelope
/// says so instead of arriving as a mailbox that suddenly holds nothing.
/// </para>
/// </remarks>
internal sealed class ScopedMailKnowledgeRetrieval
{
    /// <summary>Names the tool the model calls to look mail up.</summary>
    /// <remarks>
    /// Named after what it does rather than after the framework's default <c>Search</c>, so a model holding several tools
    /// can tell what this one searches, and so a trace of a run reads as mail retrieval rather than as an unspecified
    /// lookup.
    /// </remarks>
    internal const string SearchToolName = "search_mail";

    private const string SearchToolDescription =
        "Searches the local copy of the mailbox for messages relevant to a query and returns short extracts from them.";

    private readonly IEmailKnowledgeSearch knowledgeSearch;
    private readonly MailboxScope scope;
    private readonly MailAnsweringRunLedger runLedger;
    private readonly Lock gate = new();
    private readonly List<EmailKnowledgePassage> retrieved = [];
    private int candidateCount;
    private int relevantCandidateCount;
    private bool relevanceFilterFellBack;

    /// <summary>Initializes the retrieval one run may make.</summary>
    /// <param name="knowledgeSearch">Finds the mail relevant to a query.</param>
    /// <param name="scope">The accounts and folders every retrieval of this run is answered from.</param>
    /// <param name="runLedger">Decides how much of what a lookup found this run may still send.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    internal ScopedMailKnowledgeRetrieval(
        IEmailKnowledgeSearch knowledgeSearch,
        MailboxScope scope,
        MailAnsweringRunLedger runLedger)
    {
        ArgumentNullException.ThrowIfNull(knowledgeSearch);
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(runLedger);

        this.knowledgeSearch = knowledgeSearch;
        this.scope = scope;
        this.runLedger = runLedger;
    }

    /// <summary>Gets what this run's retrieval has reached so far, across every lookup it has made.</summary>
    /// <remarks>
    /// <para>
    /// The passages are what was handed over rather than what was found. A passage the run's ceiling would not let it
    /// send never reached the model, so citing it would name a message the answer cannot have been drawn from — while
    /// the counts beside them do say what was found, which is the only place the difference is visible.
    /// </para>
    /// <para>
    /// Readable at any point rather than only once the run has ended, because a run that failed part way through has
    /// retrieved what it retrieved and that is exactly what its record has to state.
    /// </para>
    /// </remarks>
    internal MailAnsweringRetrievalReport Report
    {
        get
        {
            // Read before the gate is taken, so this type never holds its own lock while asking for the ledger's. The
            // only other path between the two runs the same way round, which is what keeps the pair deadlock-free.
            var truncated = this.runLedger.RetrievalWasTruncated;

            lock (this.gate)
            {
                return new MailAnsweringRetrievalReport(
                    [.. this.retrieved],
                    this.candidateCount,
                    this.relevantCandidateCount,
                    Degraded(truncated, this.relevanceFilterFellBack));
            }
        }
    }

    /// <summary>Gets whether a lookup found mail this run's ceiling would not let it send.</summary>
    internal bool WasTruncated => this.runLedger.RetrievalWasTruncated;

    /// <summary>Builds the context provider the framework retrieves through.</summary>
    /// <param name="loggerFactory">Creates the logger the provider records its own decisions through.</param>
    /// <returns>The provider, which searches only when the model asks it to.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="loggerFactory" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// On demand rather than before every call, which is the difference between a question that needed no mail costing
    /// nothing and one that drags a mailbox through a provider to answer "what can you do". The model decides it needs
    /// context and asks; nothing is pushed at it.
    /// </remarks>
    internal TextSearchProvider CreateContextProvider(ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);

        var options = new TextSearchProviderOptions
        {
            SearchTime = TextSearchProviderOptions.TextSearchBehavior.OnDemandFunctionCalling,
            FunctionToolName = SearchToolName,
            FunctionToolDescription = SearchToolDescription,

            // Replaces the framework's own formatting, which writes each result as a labelled paragraph between dashed
            // separators and closes with an instruction of its own. Retrieved mail written that way is prose in the same
            // voice as an instruction, and a message imitating one of those separators is indistinguishable from it.
            ContextFormatter = this.FormatRetrieved,

            // The framework's own switch for putting queries and retrieved text into its logs. It defaults to off and is
            // set here anyway, because what it would emit is somebody's question and extracts of their mail, and a
            // default is a thing a package update may change.
            EnableSensitiveTelemetryData = false,
        };

        return new TextSearchProvider(this.SearchAsync, options, loggerFactory);
    }

    /// <summary>Answers one lookup the model asked for, with as much of what it found as this run may still send.</summary>
    /// <remarks>
    /// The result carries the passage itself as its raw representation rather than only the text, so whatever formats the
    /// context reaches the message identity and the source coordinates without searching for them again.
    /// </remarks>
    private async Task<IEnumerable<TextSearchProvider.TextSearchResult>> SearchAsync(
        string query,
        CancellationToken cancellationToken)
    {
        var found = await this.knowledgeSearch.FindPassagesAsync(this.scope, query, cancellationToken);
        var admitted = this.runLedger.AdmitPassages(found.Passages);

        lock (this.gate)
        {
            this.retrieved.AddRange(admitted);

            // Summed across the run rather than kept per lookup, because a model decides how many lookups to make and
            // a per-lookup figure would describe a decision nobody took.
            this.candidateCount += found.CandidateCount;
            this.relevantCandidateCount += found.Passages.Count;
            this.relevanceFilterFellBack |= found.RelevanceFilterFellBack;
        }

        return
        [
            .. admitted.Select(static passage => new TextSearchProvider.TextSearchResult
            {
                Text = passage.Text,
                SourceName = passage.StoredEmailId.ToString(),
                RawRepresentation = passage,
            }),
        ];
    }

    /// <summary>Writes what one lookup found into the envelope the model reads it inside.</summary>
    /// <remarks>
    /// Every result of this run was built above and carries its passage, so the formatter reaches the message identity
    /// and the source coordinates rather than the flattened strings the framework's own result type carries. The cast
    /// asserts that rather than filtering on it: a result arriving from anywhere else would otherwise be dropped from
    /// the envelope while <see cref="Report" /> still recorded it, leaving the answer citing a message the model was
    /// never shown.
    /// </remarks>
    private string FormatRetrieved(IList<TextSearchProvider.TextSearchResult> results) =>
        RetrievedMailContextFormatter.Format(
            [.. results.Select(static result => result.RawRepresentation).Cast<EmailKnowledgePassage>()],
            this.WasTruncated);

    /// <summary>Names the ways this run read less of the mailbox than an undegraded run of the same question would.</summary>
    private static MailAnsweringRunDegradation Degraded(bool truncated, bool filterFellBack) =>
        (truncated ? MailAnsweringRunDegradation.RetrievalCeilingReached : MailAnsweringRunDegradation.None)
        | (filterFellBack ? MailAnsweringRunDegradation.RelevanceFilterFellBack : MailAnsweringRunDegradation.None);
}
