// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Accounts;
using MailFathom.Application.Chat;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Retrieval.AskMail.Audit;
using MailFathom.Application.SensitiveContent.Detection;
using MailFathom.Application.SensitiveContent.Egress;
using MailFathom.Domain.Answering.Audit;

namespace MailFathom.Application.Retrieval.AskMail;

/// <summary>Answers one question about the local mailbox copy, names the emails the answer was drawn from, and records the run.</summary>
/// <remarks>
/// <para>
/// The use case owns everything between an unvalidated request and a published answer: it bounds the question, refuses
/// an account this deployment does not serve, decides whether this deployment can answer at all, admits the question
/// against what this period may still spend, cuts what one response may carry, and reports what the run did. The agent
/// behind the answering port does none of that, and no protocol adapter repeats it.
/// </para>
/// <para>
/// The scope is resolved here and again underneath. This resolution is the access decision — an account nobody
/// configured is refused before a provider is reached — and the search the run retrieves through resolves it a second
/// time on every lookup the model makes. The repetition is the point: the model writes the query and never the scope, so
/// a run that has been talked into asking about another account has the caller's own scope searched for those words.
/// </para>
/// <para>
/// It reaches no mail server. A question is answered from what synchronization has already stored, which is what keeps
/// asking one independent of IMAP availability, and there is nothing in the run that can send, delete, move, or mark
/// mail as read — a property of what the agent is composed of rather than a rule observed here.
/// </para>
/// <para>
/// Reporting the run is two things, and they are separate because they answer different questions and outlive the
/// request by different amounts. The span says how long the run took, how much it considered, and how it ended, beside
/// the tool call it happened inside; the record says which messages it read, durably, on a deployment exporting nothing.
/// Both are published however the run ended — a run that failed on its third provider call has already read somebody's
/// mail, and a report built from the answer alone would say it read nothing.
/// </para>
/// <para>
/// Neither the question nor the answer nor a citation's subject is written to a log, a span, or the record by anything on
/// this path. A question is personal data of a particularly revealing kind, and an answer is mail content restated.
/// </para>
/// <para>
/// An answer and its citations are the last point at which a run's mail content leaves this deployment, so where a
/// sensitive-content scanner is switched on both are scanned before they are published, and a scanner that cannot
/// answer refuses the response rather than serving it unscanned. What reached the model on the way there was scanned as
/// it was retrieved, which is a different egress point with a guard of its own.
/// </para>
/// </remarks>
public sealed class MailboxQuestionReader
{
    private readonly MailAnsweringCapability capability;
    private readonly MailboxScopeResolver scopeResolver;
    private readonly IMailAnsweringSpendLedger spendLedger;
    private readonly MailAnswerBounds answerBounds;
    private readonly IMailAnsweringRunTelemetry runTelemetry;
    private readonly IMailAnsweringAuditTrail auditTrail;
    private readonly TimeProvider timeProvider;
    private readonly SensitiveContentEgressGuard egressGuard;

    /// <summary>Initializes the use case.</summary>
    /// <param name="capability">Decides whether a question may run, and hands over what runs it.</param>
    /// <param name="scopeResolver">Decides which accounts and folders the answer may be drawn from.</param>
    /// <param name="spendLedger">Decides whether the current period still has an allowance for a question.</param>
    /// <param name="answerBounds">How much of one run's outcome a single answer publishes.</param>
    /// <param name="runTelemetry">Publishes the run beside the request it happened inside.</param>
    /// <param name="auditTrail">Keeps the durable record of what the run read, for the accounts that asked for one.</param>
    /// <param name="timeProvider">Stamps when the run began and when it ended.</param>
    /// <param name="egressGuard">Scans what the answer is about to publish, where this deployment scans anything.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public MailboxQuestionReader(
        MailAnsweringCapability capability,
        MailboxScopeResolver scopeResolver,
        IMailAnsweringSpendLedger spendLedger,
        MailAnswerBounds answerBounds,
        IMailAnsweringRunTelemetry runTelemetry,
        IMailAnsweringAuditTrail auditTrail,
        TimeProvider timeProvider,
        SensitiveContentEgressGuard egressGuard)
    {
        ArgumentNullException.ThrowIfNull(capability);
        ArgumentNullException.ThrowIfNull(scopeResolver);
        ArgumentNullException.ThrowIfNull(spendLedger);
        ArgumentNullException.ThrowIfNull(answerBounds);
        ArgumentNullException.ThrowIfNull(runTelemetry);
        ArgumentNullException.ThrowIfNull(auditTrail);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(egressGuard);

        this.capability = capability;
        this.scopeResolver = scopeResolver;
        this.spendLedger = spendLedger;
        this.answerBounds = answerBounds;
        this.runTelemetry = runTelemetry;
        this.auditTrail = auditTrail;
        this.timeProvider = timeProvider;
        this.egressGuard = egressGuard;
    }

    /// <summary>Answers one question from the mail within its scope.</summary>
    /// <param name="request">What the caller asked.</param>
    /// <param name="cancellationToken">Propagates caller cancellation, which ends the run and every provider call remaining in it.</param>
    /// <returns>The answer, the emails it was drawn from, and whether either had to be cut.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="request" /> is <see langword="null" />.</exception>
    /// <exception cref="MailboxQueryFilterInvalidException">Thrown when the question is blank, longer than one carries, or carries a control character, or when the scope names more values than it accepts.</exception>
    /// <exception cref="MailAccountNotAccessibleException">Thrown when the request names an account this deployment does not serve.</exception>
    /// <exception cref="MailAnsweringUnavailableException">Thrown when this deployment answers no questions, or answers them and currently cannot.</exception>
    /// <exception cref="MailAnsweringBudgetExhaustedException">Thrown when the current period has spent what this deployment allows answering to cost, or when the run reached what one question may spend.</exception>
    /// <exception cref="ChatGenerationFailedException">Thrown when the run produced no answer, naming which kind of failure ended it.</exception>
    /// <exception cref="SensitiveContentScannerUnavailableException">Thrown when a switched-on scanner could not establish what the answer carries, which refuses the response rather than serving it unscanned.</exception>
    /// <remarks>
    /// <para>
    /// The request is validated before the capability is read, so a deployment that answers questions and one that does
    /// not refuse a malformed question identically. The reverse order would let a caller learn which capabilities a
    /// server has by watching which of two refusals a broken request produces.
    /// </para>
    /// <para>
    /// The period allowance is taken after the capability is read and before the run begins, which is the last point at
    /// which nothing has been spent. Taking it earlier would count a question a deployment was never going to answer
    /// against a ceiling on what it spends.
    /// </para>
    /// <para>
    /// Nothing above that point is a run, which is why nothing above it is reported as one. A question this deployment
    /// does not serve and one the period has no allowance for both end before a model is reached, and recording either as
    /// a run that read no mail would put a refusal among the answers.
    /// </para>
    /// </remarks>
    public async Task<AskMailResult> AnswerQuestionAsync(
        AskMailRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var questionText = MailQuestionText.Create(request.QuestionText);
        // Junk is left out with no override, unlike a listing and a search. Answering a question is exactly the path the
        // exclusion exists for: content written to manipulate whoever reads it now has a model reading it, and a caller
        // hunting a wrongly filed message uses the listing or the search that can ask for it.
        var scope = this.scopeResolver.ReadableScope(
            request.Accounts,
            request.Folders,
            JunkMailInclusion.Excluded);

        var gate = await this.capability.ResolveAsync(cancellationToken);
        if (gate.Answerer is not { } answerer)
        {
            throw gate.Availability is MailAnsweringAvailability.Inactive
                ? MailAnsweringUnavailableException.NotServed()
                : MailAnsweringUnavailableException.TemporarilyUnable();
        }

        if (!this.spendLedger.TryAdmitRun())
        {
            throw MailAnsweringBudgetExhaustedException.PeriodSpent();
        }

        var startedAt = this.timeProvider.GetUtcNow();
        var observation = new MailAnsweringRunObservation(
            MailAnsweringRunId.Create(Guid.CreateVersion7(startedAt)),
            scope,
            startedAt);

        try
        {
            return await this.ConductAsync(
                answerer,
                new MailQuestion(questionText, scope),
                observation,
                cancellationToken);
        }
        finally
        {
            // Outside the span rather than inside it, so the duration reported is the run's own and not the run plus a
            // database write. The record is owed whatever the run did, which is why it is a finally and not a
            // continuation of the successful path.
            await this.auditTrail.RecordAsync(observation, cancellationToken);
        }
    }

    /// <summary>Conducts one run inside its span, and stamps how it ended before the span is published.</summary>
    /// <remarks>
    /// Every ending is stamped by a handler that re-raises what it caught, so the failure reaches the caller unchanged
    /// and the report reaches an operator complete. The alternative — stamping after the exception has escaped — would
    /// close the span before anything knew how the run ended.
    /// </remarks>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "The exception is re-raised unchanged; the handler exists only to stamp how the run ended before its span is published.")]
    private async Task<AskMailResult> ConductAsync(
        IMailQuestionAnswerer answerer,
        MailQuestion question,
        MailAnsweringRunObservation observation,
        CancellationToken cancellationToken)
    {
        using var runReport = this.runTelemetry.BeginRun(observation);

        try
        {
            var answer = await answerer.AnswerAsync(question, observation, cancellationToken);
            var published = await this.PublishedAsync(answer, observation.Retrieval, cancellationToken);

            observation.RecordOutcome(
                MailAnsweringRunOutcome.Answered,
                [.. published.Citations.Select(static citation => citation.StoredEmailId)],
                this.timeProvider.GetUtcNow());

            return published;
        }
        catch (OperationCanceledException)
        {
            this.RecordEnding(observation, MailAnsweringRunOutcome.Cancelled);

            throw;
        }
        catch (MailAnsweringBudgetExhaustedException)
        {
            this.RecordEnding(observation, MailAnsweringRunOutcome.RunBudgetExhausted);

            throw;
        }
        catch (ChatGenerationFailedException generation)
        {
            this.RecordEnding(
                observation,
                generation.Failure is ChatGenerationFailure.AnswerEmpty
                    ? MailAnsweringRunOutcome.AnswerEmpty
                    : MailAnsweringRunOutcome.ProviderFailed);

            throw;
        }
        catch (Exception)
        {
            this.RecordEnding(observation, MailAnsweringRunOutcome.Failed);

            throw;
        }
    }

    /// <summary>Stamps an ending that published no answer, and therefore cited nothing.</summary>
    private void RecordEnding(MailAnsweringRunObservation observation, MailAnsweringRunOutcome outcome) =>
        observation.RecordOutcome(outcome, [], this.timeProvider.GetUtcNow());

    /// <summary>Cuts one run's outcome down to what a single answer publishes, and says what it cut.</summary>
    /// <remarks>
    /// <para>
    /// An answer is the last point at which a run's mail content leaves this deployment, and it is scanned here even
    /// though the extracts it was written from were scanned on their way to the model. A model restates what it read,
    /// and an answer is the one text in a run that nothing else has looked at.
    /// </para>
    /// <para>
    /// Scanning happens before the answer is cut to what one response carries, so the published text is bounded after
    /// every placeholder is in it. Cutting first would bound a text that is not the one published.
    /// </para>
    /// </remarks>
    private async Task<AskMailResult> PublishedAsync(
        MailAnswer answer,
        MailAnsweringRetrievalReport retrieval,
        CancellationToken cancellationToken)
    {
        var maximumCitations = this.answerBounds.MaximumCitations;
        var maximumAnswerCharacters = this.answerBounds.MaximumAnswerCharacters;

        var answerText = await this.egressGuard.GuardAsync(
            SensitiveContentEgressPoint.McpSnippet,
            answer.Text,
            cancellationToken);

        // Collapsed and counted before anything is scanned, because the count decides the truncation flag while only
        // the citations that survive the bound are published — and a subject nobody will read is a scan nobody needs.
        EmailKnowledgePassage[] citable = [.. retrieval.Passages.DistinctBy(static passage => passage.StoredEmailId)];
        var citations = await this.CitedAsync(citable.Take(maximumCitations), cancellationToken);

        return new AskMailResult(
            Bounded(answerText, maximumAnswerCharacters),
            citations,
            answerText.Length > maximumAnswerCharacters,
            citable.Length > maximumCitations,
            retrieval.Degradation.HasFlag(MailAnsweringRunDegradation.RetrievalCeilingReached));
    }

    /// <summary>Reads the passages one answer cites into citations, with the one text a citation carries scanned.</summary>
    /// <remarks>
    /// <para>
    /// The passages arrive collapsed by the identity they are traced through and cut to what one response carries, so
    /// this scans exactly what is published: a message reached by three lookups costs one scan, and a message the bound
    /// dropped costs none. The order is the one the run first reached each message in, rather than one nothing produced.
    /// </para>
    /// <para>
    /// The subject is the whole of a citation's mail content: the identity, the account, the folder alias, and the
    /// received instant are what a reader opens the message by. The scan is here rather than left to the retrieval
    /// because a citation is published to the caller while an extract is sent to a model, which are two egress points
    /// that happen to share a value.
    /// </para>
    /// </remarks>
    private async Task<IReadOnlyList<MailAnswerCitation>> CitedAsync(
        IEnumerable<EmailKnowledgePassage> passages,
        CancellationToken cancellationToken)
    {
        var cited = new List<MailAnswerCitation>();

        foreach (var passage in passages)
        {
            cited.Add(new MailAnswerCitation(
                passage.StoredEmailId,
                passage.AccountId,
                passage.FolderAlias,
                await this.egressGuard.GuardOptionalAsync(
                    SensitiveContentEgressPoint.McpSnippet,
                    passage.Subject,
                    cancellationToken),
                passage.ReceivedAt));
        }

        return cited;
    }

    /// <summary>Cuts an answer longer than one response carries.</summary>
    /// <remarks>
    /// A cut that would fall between the halves of a surrogate pair takes the whole pair instead, for the reason a
    /// passage's own cut does: an answer written about mail carries every script mail does, and a lone surrogate is not
    /// text — it survives no serialization this value is about to cross.
    /// </remarks>
    private static string Bounded(string text, int maximumLength)
    {
        if (text.Length <= maximumLength)
        {
            return text;
        }

        return text[..(char.IsLowSurrogate(text[maximumLength]) ? maximumLength - 1 : maximumLength)];
    }
}
