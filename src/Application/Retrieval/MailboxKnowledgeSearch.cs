// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Emails.Search;
using MailFathom.Application.Emails.SearchEmails;

namespace MailFathom.Application.Retrieval;

/// <summary>Answers a retrieval from the mailbox search this deployment already serves.</summary>
/// <remarks>
/// <para>
/// The whole of the retrieval side, and deliberately thin: hybrid ranking, the structured filters, the account
/// restriction, and the extracts themselves all belong to <see cref="MailboxSearchReader" />, which is the same
/// retrieval a caller reaches through <c>search_emails</c>. Answering a question therefore cannot see mail that
/// searching for it would not, cannot narrow in a way searching could not, and improving the ranking improves both.
/// </para>
/// <para>
/// Every filter is handed on unvalidated and unnormalized, which is the point rather than an omission: the use case
/// below refuses an unusable address, an over-long subject fragment, and a date range that can select nothing, in the
/// same words and under the same error code whoever wrote them. A second validation here would be a second set of rules
/// to keep in agreement with the first.
/// </para>
/// <para>
/// What this adds is the shape a model's context needs rather than a reader's: one bounded extract per message instead
/// of a summary and several highlighted fragments, carrying only the identity an answer is traced through. Everything a
/// listing publishes and an answer does not need — the participants, the size, the flags, the attachment summary — is
/// dropped here rather than sent to a provider.
/// </para>
/// </remarks>
public sealed class MailboxKnowledgeSearch : IEmailKnowledgeSearch
{
    /// <summary>Separates the extracts of one message where several were cut from it.</summary>
    /// <remarks>
    /// A line break rather than a joining word: the fragments are discontiguous parts of one body, and any word placed
    /// between them would be text this system wrote inside content it did not.
    /// </remarks>
    private const char SnippetSeparator = '\n';

    private readonly MailboxSearchReader searchReader;
    private readonly EmailKnowledgeBounds bounds;

    /// <summary>Initializes the retrieval over the mailbox search and the bounds it hands its results over under.</summary>
    /// <param name="searchReader">Ranks and cuts the mail a query matched.</param>
    /// <param name="bounds">How much of what it found may be handed over.</param>
    /// <exception cref="ArgumentNullException">Thrown when either argument is <see langword="null" />.</exception>
    public MailboxKnowledgeSearch(MailboxSearchReader searchReader, EmailKnowledgeBounds bounds)
    {
        ArgumentNullException.ThrowIfNull(searchReader);
        ArgumentNullException.ThrowIfNull(bounds);

        this.searchReader = searchReader;
        this.bounds = bounds;
    }

    /// <inheritdoc />
    public async Task<EmailKnowledgeLookup> FindPassagesAsync(
        MailboxScope scope,
        EmailKnowledgeQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(query);

        var request = new SearchEmailsRequest
        {
            QueryText = query.QueryText,

            // The scope is the caller's and overwrites nothing the query carries, because the query carries no scope at
            // all. That is what makes the boundary a property of these two types rather than of this assignment. It is
            // handed over already resolved rather than written back out as accounts and folders, because a folder the
            // question named by role has resolved to a different alias on each account and naming those aliases again
            // would let each of them mean whichever account carries it.
            ResolvedScope = scope,
            SenderAddress = query.SenderAddress,
            RecipientAddress = query.RecipientAddress,
            SubjectFragment = query.SubjectFragment,
            ReceivedOnOrAfter = query.ReceivedOnOrAfter,
            ReceivedBefore = query.ReceivedBefore,
            IsRemotelySeen = query.IsRemotelySeen,
            HasAttachments = query.HasAttachments,
            ResultLimit = this.bounds.MaximumPassages,
        };

        // Deliberately the unguarded window: an extract reaches a model rather than an MCP caller, and the retrieval
        // that sends it guards it there, under the egress point it actually crosses.
        var result = await this.searchReader.SearchWindowAsync(request, cancellationToken);

        return EmailKnowledgeLookup.Unfiltered(
            [
                .. result.Matches
                    .Select(this.ToPassage)
                    .Where(static passage => passage.Text.Length is not 0),
            ],
            result.RetrievalMode);
    }

    /// <summary>Reads one match into the passage a model receives.</summary>
    /// <remarks>
    /// A message whose extracts are empty produces a passage with no text, which the caller drops. That is a message
    /// matched on its subject or its participants while its body yielded none — encrypted mail, or mail whose content
    /// lives in an attachment — and sending an identifier with nothing beside it would spend context on a message the
    /// model cannot read a word of.
    /// </remarks>
    private EmailKnowledgePassage ToPassage(EmailSearchMatch match)
    {
        var summary = match.Summary;

        return new EmailKnowledgePassage
        {
            StoredEmailId = summary.StoredEmailId,
            AccountId = summary.AccountId,
            FolderAlias = summary.FolderAlias,
            Subject = summary.Subject,
            ReceivedAt = summary.ReceivedAt,
            SenderVerification = summary.SenderVerification,
            Text = this.Bounded(string.Join(SnippetSeparator, match.Snippets)),
        };
    }

    /// <summary>Cuts an extract to the size one passage may carry.</summary>
    /// <remarks>
    /// A cut that would fall between the halves of a surrogate pair takes the whole pair instead. Mail carries emoji and
    /// every script outside the basic plane, and a lone surrogate is not text: it survives no serialization the passage
    /// is about to cross, and what a provider would receive is a replacement character or a refused request.
    /// </remarks>
    private string Bounded(string text)
    {
        var limit = this.bounds.MaximumCharactersPerPassage;

        if (text.Length <= limit)
        {
            return text;
        }

        return text[..(char.IsLowSurrogate(text[limit]) ? limit - 1 : limit)];
    }
}
