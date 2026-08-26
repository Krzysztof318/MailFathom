// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Search;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;

namespace MailFathom.Application.Emails.BrowseSearch;

/// <summary>What a screen asks for when somebody searches their mail.</summary>
/// <remarks>
/// <para>
/// This is the unvalidated contract, exactly as <see cref="SearchEmails.SearchEmailsRequest" /> is for the tool search:
/// nothing here has been bounded, normalized, or checked against the accounts the caller's owner owns, and
/// <see cref="MailSearchBrowser" /> is what does that.
/// </para>
/// <para>
/// Every field beside <see cref="QueryText" /> is a constraint rather than a hint. A person who filtered to one sender
/// and to last year has said which mail may be returned, not which mail they would prefer; the query decides the order
/// of what is left and can never put back what a filter excluded.
/// </para>
/// <para>
/// Nothing here selects how the search is ranked. Whether an instance ranks by meaning as well as by words is the
/// deployment's decision and the page reports what happened — a request able to ask for the lexical ranking of a hybrid
/// instance would be asking for worse results with no way to know it.
/// </para>
/// </remarks>
public sealed record BrowseSearchRequest
{
    /// <summary>Gets the text to search for.</summary>
    /// <remarks>Required: a search with no text is a list, which the timeline read model answers in a stable order and with a cursor in both directions.</remarks>
    public string? QueryText { get; init; }

    /// <summary>Gets the accounts to search, or empty for every account the caller's owner owns.</summary>
    public IReadOnlyList<MailAccountSelector> Accounts { get; init; } = [];

    /// <summary>Gets the folders to search, or empty for every folder of those accounts.</summary>
    public IReadOnlyList<MailFolderReference> Folders { get; init; } = [];

    /// <summary>Gets whether the account's junk folder is searched too, which it is not unless the screen asks.</summary>
    public bool IncludeJunkMail { get; init; }

    /// <summary>Gets the address the sender must carry, in any case, or <see langword="null" /> for any sender.</summary>
    public string? SenderAddress { get; init; }

    /// <summary>Gets the address a <c>To</c> or <c>Cc</c> recipient must carry, or <see langword="null" /> for any recipient.</summary>
    public string? RecipientAddress { get; init; }

    /// <summary>Gets the inclusive start of the received range, or <see langword="null" /> for no start.</summary>
    public DateTimeOffset? ReceivedOnOrAfter { get; init; }

    /// <summary>Gets the exclusive end of the received range, or <see langword="null" /> for no end.</summary>
    public DateTimeOffset? ReceivedBefore { get; init; }

    /// <summary>Gets the remote <c>\Seen</c> state to require, or <see langword="null" /> for either.</summary>
    public bool? IsRemotelySeen { get; init; }

    /// <summary>Gets the remote <c>\Flagged</c> state to require, or <see langword="null" /> for either.</summary>
    public bool? IsRemotelyFlagged { get; init; }

    /// <summary>Gets whether attachments are required, or <see langword="null" /> for either.</summary>
    public bool? HasAttachments { get; init; }

    /// <summary>Gets how many results one page returns, or <see langword="null" /> to take the default.</summary>
    /// <remarks>An absent page size takes <see cref="EmailSearchResultLimit.DefaultValue" />; a named one outside the accepted range is refused rather than clamped.</remarks>
    public int? PageSize { get; init; }

    /// <summary>Gets the cursor a previous page returned, or <see langword="null" /> to read the best-ranked end of the list.</summary>
    /// <remarks>The cursor is opaque and belongs to the query and the filters it was issued for; presenting it against a different search is refused.</remarks>
    public string? Cursor { get; init; }
}
