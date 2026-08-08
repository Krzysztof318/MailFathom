// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Accounts;
using MailFathom.Application.Chat;
using MailFathom.Application.Emails.Mailboxes;

namespace MailFathom.Application.Retrieval.AskMail;

/// <summary>Answers one question about the local mailbox copy and names the emails the answer was drawn from.</summary>
/// <remarks>
/// <para>
/// The use case owns everything between an unvalidated request and a published answer: it bounds the question, refuses
/// an account this deployment does not serve, decides whether this deployment can answer at all, admits the question
/// against what this period may still spend, and cuts what one response may carry. The agent behind the answering port
/// does none of that, and no protocol adapter repeats it.
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
/// Neither the question nor the answer nor a citation's subject is written to a log by anything on this path. A question
/// is personal data of a particularly revealing kind, and an answer is mail content restated.
/// </para>
/// </remarks>
public sealed class MailboxQuestionReader
{
    private readonly MailAnsweringCapability capability;
    private readonly MailboxScopeResolver scopeResolver;
    private readonly IMailAnsweringSpendLedger spendLedger;
    private readonly MailAnswerBounds answerBounds;

    /// <summary>Initializes the use case.</summary>
    /// <param name="capability">Decides whether a question may run, and hands over what runs it.</param>
    /// <param name="scopeResolver">Decides which accounts and folders the answer may be drawn from.</param>
    /// <param name="spendLedger">Decides whether the current period still has an allowance for a question.</param>
    /// <param name="answerBounds">How much of one run's outcome a single answer publishes.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public MailboxQuestionReader(
        MailAnsweringCapability capability,
        MailboxScopeResolver scopeResolver,
        IMailAnsweringSpendLedger spendLedger,
        MailAnswerBounds answerBounds)
    {
        ArgumentNullException.ThrowIfNull(capability);
        ArgumentNullException.ThrowIfNull(scopeResolver);
        ArgumentNullException.ThrowIfNull(spendLedger);
        ArgumentNullException.ThrowIfNull(answerBounds);

        this.capability = capability;
        this.scopeResolver = scopeResolver;
        this.spendLedger = spendLedger;
        this.answerBounds = answerBounds;
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
    /// </remarks>
    public async Task<AskMailResult> AnswerQuestionAsync(
        AskMailRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var questionText = MailQuestionText.Create(request.QuestionText);
        var scope = this.scopeResolver.ReadableScope(request.AccountIds, request.FolderAliases);

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

        var answer = await answerer.AnswerAsync(new MailQuestion(questionText, scope), cancellationToken);

        return this.Published(answer);
    }

    /// <summary>Cuts one run's outcome down to what a single answer publishes, and says what it cut.</summary>
    private AskMailResult Published(MailAnswer answer)
    {
        var citations = Cited(answer.Passages);
        var maximumCitations = this.answerBounds.MaximumCitations;
        var maximumAnswerCharacters = this.answerBounds.MaximumAnswerCharacters;

        return new AskMailResult(
            Bounded(answer.Text, maximumAnswerCharacters),
            [.. citations.Take(maximumCitations)],
            answer.Text.Length > maximumAnswerCharacters,
            citations.Count > maximumCitations,
            answer.RetrievalWasTruncated);
    }

    /// <summary>Reads the passages a run retrieved into one citation per email.</summary>
    /// <remarks>
    /// A run makes several lookups and one message can answer more than one of them, so the passages are collapsed by
    /// the identity they are traced through. The first occurrence is kept, which leaves the citations in the order the
    /// run first reached each message rather than in an order nothing produced.
    /// </remarks>
    private static IReadOnlyList<MailAnswerCitation> Cited(IReadOnlyList<EmailKnowledgePassage> passages) =>
    [
        .. passages
            .DistinctBy(static passage => passage.StoredEmailId)
            .Select(static passage => new MailAnswerCitation(
                passage.StoredEmailId,
                passage.AccountId,
                passage.FolderAlias,
                passage.Subject,
                passage.ReceivedAt)),
    ];

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
