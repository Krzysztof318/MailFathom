// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel;
using MailFathom.AI.Orchestration;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Retrieval;
using MailFathom.Application.Retrieval.AskMail;
using MailFathom.Application.SensitiveContent.Egress;
using MailFathom.Domain.Answering.Audit;
using Microsoft.Extensions.AI;

namespace MailFathom.AI.Retrieval;

/// <summary>The mail one run may reach, and the record of what it reached.</summary>
/// <remarks>
/// <para>
/// This is where the scope stops being negotiable. The tool the framework is handed takes a query and the narrowing a
/// search publishes, and it takes no accounts and no folders at all, because those were bound into this object when the
/// run was composed: a model can write any lookup it likes and every one of them is answered from the same scope.
/// Nothing in an instruction, a retrieved message, or a tool argument reaches the value, which is what makes the
/// boundary structural rather than a rule the prompt asks for.
/// </para>
/// <para>
/// The filters beside the query are the other half of that reading, and they are why this type publishes a tool of its
/// own rather than the framework's text-search provider. That provider offers a query and nothing else, which leaves a
/// question that is naturally a filter — mail from one person, mail of one week, mail carrying an attachment — to be
/// answered by ranking free text across the whole scope, and that is the one shape lexical and vector similarity are
/// both weakest at. What is exposed is exactly what <c>search_emails</c> publishes minus the two arguments a model may
/// not hold: the scope, which is the caller's authorization, and the result count, which is the deployment's bound on
/// how much mail one lookup draws out.
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

    /// <summary>Names the tool's one required argument, which is the text every lookup ranks against.</summary>
    /// <remarks>Published as a constant because the framework reads it off a parameter, and a test scripting a tool call has to name the same thing the schema does.</remarks>
    internal const string QueryArgumentName = "queryText";

    /// <summary>What the model is told the tool does, and what it needs in order to write a usable lookup.</summary>
    /// <remarks>
    /// <para>
    /// Deliberately as detailed as the description <c>search_emails</c> publishes to its own callers, and for the same
    /// reason: a client writes a better query partly because it was told how the query is read. A model given one
    /// sentence writes prose at a ranking that matches words, discovers nothing, and reports that the mailbox does not
    /// answer the question.
    /// </para>
    /// <para>
    /// What it does not state is which ranking is in force, because that is a property of the instance at the moment of
    /// the lookup rather than of this build — an instance whose embedding provider is refusing ranks lexically until it
    /// is not. The envelope carries it per lookup instead, and this text says where to read it.
    /// </para>
    /// </remarks>
    private const string SearchToolDescription =
        "Searches the local copy of this mailbox for messages relevant to a query and returns bounded extracts of the "
        + "body around the matched words. Retrieval is lexical or hybrid depending on how this server is configured, "
        + $"and every result says which in the {RetrievedMailContextFormatter.RetrievalModeAttributeName} attribute of "
        + $"the <{RetrievedMailContextFormatter.RetrievalElementName}> element: lexical finds the words a query "
        + "contains rather than what they mean, while hybrid also finds mail whose meaning is close and combines the "
        + "two rankings. Words that appear only inside an attachment are never searchable either way. Narrow with the "
        + "filters rather than by describing the narrowing in the query: a question about one person's mail, one period, "
        + "or mail carrying an attachment is answered far better by the matching filter than by words that have to rank. "
        + "Which accounts and folders are searched is fixed by the caller and is not yours to set. Each call returns one "
        + "window of the most relevant results that nothing continues, so call it again with a different query or "
        + "narrower filters to reach other mail. Matching nothing is an ordinary empty result rather than an error.";

    private readonly IEmailKnowledgeSearch knowledgeSearch;
    private readonly MailboxScope scope;
    private readonly MailAnsweringRunLedger runLedger;
    private readonly SensitiveContentEgressGuard egressGuard;
    private readonly Lock gate = new();
    private readonly List<EmailKnowledgePassage> retrieved = [];
    private int candidateCount;
    private int relevantCandidateCount;
    private bool relevanceFilterFellBack;

    /// <summary>Initializes the retrieval one run may make.</summary>
    /// <param name="knowledgeSearch">Finds the mail relevant to a query.</param>
    /// <param name="scope">The accounts and folders every retrieval of this run is answered from.</param>
    /// <param name="runLedger">Decides how much of what a lookup found this run may still send.</param>
    /// <param name="egressGuard">Scans every extract before it is written into the envelope a model reads.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    internal ScopedMailKnowledgeRetrieval(
        IEmailKnowledgeSearch knowledgeSearch,
        MailboxScope scope,
        MailAnsweringRunLedger runLedger,
        SensitiveContentEgressGuard egressGuard)
    {
        ArgumentNullException.ThrowIfNull(knowledgeSearch);
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(runLedger);
        ArgumentNullException.ThrowIfNull(egressGuard);

        this.knowledgeSearch = knowledgeSearch;
        this.scope = scope;
        this.runLedger = runLedger;
        this.egressGuard = egressGuard;
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

    /// <summary>Builds the one tool the run is composed with.</summary>
    /// <returns>The tool, which searches only when the model calls it.</returns>
    /// <remarks>
    /// On demand rather than before every call, which is the difference between a question that needed no mail costing
    /// nothing and one that drags a mailbox through a provider to answer "what can you do". The model decides it needs
    /// context and asks; nothing is pushed at it.
    /// </remarks>
    internal AIFunction CreateSearchTool() =>
        AIFunctionFactory.Create(this.SearchAsync, SearchToolName, SearchToolDescription);

    /// <summary>Answers one lookup the model asked for, with as much of what it found as this run may still send.</summary>
    /// <remarks>
    /// <para>
    /// The result is the envelope itself rather than a list the framework then formats. Writing it here is what keeps
    /// retrieved mail inside a document this system controls: the framework's own formatting writes each result as a
    /// labelled paragraph between dashed separators and closes with an instruction of its own, and mail written that way
    /// is prose in the same voice as an instruction.
    /// </para>
    /// <para>
    /// A refused filter comes back as a document saying which one, because the caller here is a tool loop and a model
    /// that wrote an unusable value can write a usable one. Absorbing it into an empty envelope would tell the model the
    /// mailbox holds nothing, and raising it would end a run over a value the model was free to correct. The refusal
    /// names the filter and never the value, which is a property of the failure rather than of this method.
    /// </para>
    /// </remarks>
    private async Task<string> SearchAsync(
        [Description("The text to rank mail against, up to 512 characters. Quoted phrases, OR, and a leading - to exclude a word are understood; every other punctuation mark is ordinary text. Write the words you expect the mail itself to contain rather than the question you were asked, in the language that mail is likely written in: matching compares words rather than translating them, so a mailbox holding several languages is reached by a lookup per language.")]
        string queryText,
        [Description("Return only mail sent from this mail address. Matched as a whole address rather than as a fragment, without regard to case. Omit to match any sender.")]
        string? senderAddress = null,
        [Description("Return only mail addressed to this mail address in its To or Cc header. Matched as a whole address rather than as a fragment; Reply-To is not searched. Omit to match any recipient.")]
        string? recipientAddress = null,
        [Description("Return only mail whose subject contains this text, without regard to case, up to 256 characters. This narrows which mail is eligible before any of it is ranked and is unrelated to queryText. Omit to match any subject.")]
        string? subjectFragment = null,
        [Description("Return only mail received at or after this ISO 8601 timestamp. Mail whose received date is unknown is excluded whenever either bound is named. Omit for no lower bound.")]
        DateTimeOffset? receivedOnOrAfter = null,
        [Description("Return only mail received strictly before this ISO 8601 timestamp. Omit for no upper bound.")]
        DateTimeOffset? receivedBefore = null,
        [Description("Return only mail the mail server last reported as read (true) or unread (false). Omit to match either. Searching never changes this state.")]
        bool? isRemotelySeen = null,
        [Description("Return only mail that carries attachments (true) or that carries none (false). Omit to match either. Inline images and cryptographic signature parts do not count as attachments.")]
        bool? hasAttachments = null,
        CancellationToken cancellationToken = default)
    {
        var query = new EmailKnowledgeQuery
        {
            QueryText = queryText,
            SenderAddress = senderAddress,
            RecipientAddress = recipientAddress,
            SubjectFragment = subjectFragment,
            ReceivedOnOrAfter = receivedOnOrAfter,
            ReceivedBefore = receivedBefore,
            IsRemotelySeen = isRemotelySeen,
            HasAttachments = hasAttachments,
        };

        EmailKnowledgeLookup found;

        try
        {
            found = await this.knowledgeSearch.FindPassagesAsync(this.scope, query, cancellationToken);
        }
        catch (MailboxQueryFilterInvalidException refusal)
        {
            return RetrievedMailContextFormatter.FormatRefusal(refusal.FilterName, refusal.Message);
        }

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

        return RetrievedMailContextFormatter.Format(
            await this.GuardedAsync(admitted, cancellationToken),
            found.RetrievalMode,
            this.WasTruncated);
    }

    /// <summary>Scans every extract this lookup is about to hand to a model.</summary>
    /// <remarks>
    /// <para>
    /// This is where mail reaches a chat provider. The instruction the run carries is a constant of the build and the
    /// question is guarded where the run begins, so the extracts are the remaining half of what a prompt is composed
    /// from — and a tool this agent gains later is a new egress point that takes a guard of its own rather than
    /// inheriting this one.
    /// </para>
    /// <para>
    /// The extract and the subject are guarded, and the envelope is written afterwards from the guarded values. Guarding
    /// the written envelope instead would let one detection cover the end of an extract and the element that closes it,
    /// and replacing that region would take the document's structure with it.
    /// </para>
    /// <para>
    /// What the run reports having retrieved is left as it was found. The report is read in this process — by the record
    /// of what the run read, and by the citations the answer publishes, which are guarded where they are published — so
    /// redacting it here would guard nothing and would make one message read two ways inside one run.
    /// </para>
    /// </remarks>
    private async Task<IReadOnlyList<EmailKnowledgePassage>> GuardedAsync(
        IReadOnlyList<EmailKnowledgePassage> passages,
        CancellationToken cancellationToken)
    {
        if (!this.egressGuard.IsActive)
        {
            return passages;
        }

        var guarded = new List<EmailKnowledgePassage>(passages.Count);

        foreach (var passage in passages)
        {
            guarded.Add(passage with
            {
                Subject = await this.egressGuard.GuardOptionalAsync(
                    SensitiveContentEgressPoint.ChatPrompt,
                    passage.Subject,
                    cancellationToken),
                Text = await this.egressGuard.GuardAsync(
                    SensitiveContentEgressPoint.ChatPrompt,
                    passage.Text,
                    cancellationToken),
            });
        }

        return guarded;
    }

    /// <summary>Names the ways this run read less of the mailbox than an undegraded run of the same question would.</summary>
    private static MailAnsweringRunDegradation Degraded(bool truncated, bool filterFellBack) =>
        (truncated ? MailAnsweringRunDegradation.RetrievalCeilingReached : MailAnsweringRunDegradation.None)
        | (filterFellBack ? MailAnsweringRunDegradation.RelevanceFilterFellBack : MailAnsweringRunDegradation.None);
}
