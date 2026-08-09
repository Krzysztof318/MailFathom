// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Emails.Search;
using MailFathom.Application.Emails.SearchEmails;
using MailFathom.Domain.Accounts;

namespace MailFathom.Application.Retrieval;

/// <summary>Answers a retrieval from the mailbox search this deployment already serves.</summary>
/// <remarks>
/// <para>
/// The whole of the retrieval side, and deliberately thin: hybrid ranking, the account restriction, and the extracts
/// themselves all belong to <see cref="MailboxSearchReader" />, which is the same retrieval a caller reaches through
/// <c>search_emails</c>. Answering a question therefore cannot see mail that searching for it would not, and improving
/// the ranking improves both.
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
        string queryText,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);

        if (UsableQueryText(queryText) is not { } validatedQueryText)
        {
            return EmailKnowledgeLookup.Unfiltered([]);
        }

        var request = new SearchEmailsRequest
        {
            QueryText = validatedQueryText.Value,
            Accounts = [.. scope.AccountIds.Select(MailAccountSelector.For)],
            FolderAliases = scope.FolderAliases,
            ResultLimit = this.bounds.MaximumPassages,
        };

        var result = await this.searchReader.SearchEmailsAsync(request, cancellationToken);

        return EmailKnowledgeLookup.Unfiltered(
        [
            .. result.Matches
                .Select(this.ToPassage)
                .Where(static passage => passage.Text.Length is not 0),
        ]);
    }

    /// <summary>Validates the one part of a retrieval a model wrote, treating text no query accepts as no result.</summary>
    /// <remarks>
    /// <para>
    /// The asymmetry this method exists for: the query is free-form model output, while the scope beside it is the
    /// caller's own authorization. So unusable text is a retrieval that found nothing — nobody can be told to correct
    /// it, and a run whose lookup found nothing still has an answer to give — while an unusable scope stays a failure
    /// and still travels to the caller that supplied it.
    /// </para>
    /// <para>
    /// It asks the query text's own type rather than restating its rules, which is why blank text, text longer than one
    /// query carries, and text holding a character no document could are all one answer here. A rule added there is
    /// covered here without this method learning about it.
    /// </para>
    /// </remarks>
    private static EmailSearchQueryText? UsableQueryText(string queryText)
    {
        try
        {
            return EmailSearchQueryText.Create(queryText);
        }
        catch (MailboxQueryFilterInvalidException)
        {
            return null;
        }
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
