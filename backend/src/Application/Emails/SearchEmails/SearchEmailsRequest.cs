// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Emails.Search;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;

namespace MailFathom.Application.Emails.SearchEmails;

/// <summary>What a caller asks for when searching the local mailbox copy for text.</summary>
/// <remarks>
/// <para>
/// This is the unvalidated contract: every value is what a caller supplied, and nothing here has been normalized,
/// bounded, or checked against the accounts this deployment serves. <see cref="MailboxSearchReader" /> does that and
/// produces the validated <see cref="MailboxEmailSelection" /> and <see cref="EmailSearchQueryText" /> the read port
/// receives, so no protocol adapter can reach a query with either one unvalidated.
/// </para>
/// <para>
/// The structured filters are the ones a listing takes, and they mean the same things — including attachment presence,
/// which follows the MIME classification rather than a header. What a listing has and this does not is a reading
/// direction and a cursor: a ranked window has neither.
/// </para>
/// </remarks>
public sealed record SearchEmailsRequest
{
    /// <summary>Gets the text to search for.</summary>
    /// <remarks>Required: a search with no text is a listing, which the timeline read model answers in a stable order and with a cursor.</remarks>
    public string? QueryText { get; init; }

    /// <summary>Gets the text naming the accounts to search, or empty for every account this deployment serves.</summary>
    /// <remarks>An account may be named by its configured identifier or by the display name it is published under, and the use case settles which against the accounts it serves.</remarks>
    public IReadOnlyList<MailAccountSelector> Accounts { get; init; } = [];

    /// <summary>Gets the folders to search, or empty for every folder of the named accounts.</summary>
    /// <remarks>A folder may be named by its alias or by the role it plays, and the use case settles which folder of which account each one means.</remarks>
    public IReadOnlyList<MailFolderReference> Folders { get; init; } = [];

    /// <summary>Gets whether the account's junk folder is searched too, which it is not unless the caller asks.</summary>
    /// <remarks>
    /// Naming the junk folder in <see cref="Folders" /> is not asking, for the reason a listing states: the folder list
    /// narrows the readable folders and this decides which are readable at all.
    /// </remarks>
    public bool IncludeJunkMail { get; init; }

    /// <summary>Gets the address the sender must carry, in any case, or <see langword="null" /> for any sender.</summary>
    public string? SenderAddress { get; init; }

    /// <summary>Gets the address a <c>To</c> or <c>Cc</c> recipient must carry, or <see langword="null" /> for any recipient.</summary>
    public string? RecipientAddress { get; init; }

    /// <summary>Gets the fragment the subject must contain, compared without regard to case, or <see langword="null" /> for any subject.</summary>
    /// <remarks>A structured filter over the stored subject, unrelated to the free-text query: it narrows which emails are eligible before any of them is ranked.</remarks>
    public string? SubjectFragment { get; init; }

    /// <summary>Gets the inclusive start of the received range, or <see langword="null" /> for no start.</summary>
    public DateTimeOffset? ReceivedOnOrAfter { get; init; }

    /// <summary>Gets the exclusive end of the received range, or <see langword="null" /> for no end.</summary>
    public DateTimeOffset? ReceivedBefore { get; init; }

    /// <summary>Gets the remote <c>\Seen</c> state to require, or <see langword="null" /> for either.</summary>
    public bool? IsRemotelySeen { get; init; }

    /// <summary>Gets the remote <c>\Flagged</c> state to require, or <see langword="null" /> for either.</summary>
    public bool? IsRemotelyFlagged { get; init; }

    /// <summary>Gets the keyword an email must carry, in any case, or <see langword="null" /> for any keyword.</summary>
    public string? Keyword { get; init; }

    /// <summary>Gets whether attachments are required, or <see langword="null" /> for either.</summary>
    public bool? HasAttachments { get; init; }

    /// <summary>Gets the scope a caller has already resolved, which is read instead of <see cref="Accounts" /> and <see cref="Folders" />.</summary>
    /// <remarks>
    /// Internal because resolving a scope is what refuses an account this deployment does not serve and withholds a
    /// folder no tool may read, so a protocol adapter must never be able to hand one in ready-made. The one caller that
    /// may is a use case that resolved its own scope through <see cref="MailboxScopeResolver" /> and then searches with
    /// it: an answering run resolves once and searches several times. Re-resolving there would be wrong rather than
    /// merely repeated, because a folder named by role resolves to a different alias on each account and re-reading
    /// those aliases would mean whichever account carries the name.
    /// </remarks>
    internal MailboxScope? ResolvedScope { get; init; }

    /// <summary>Gets how many ranked results to return, or <see langword="null" /> to take the default.</summary>
    /// <remarks>An absent count takes <see cref="EmailSearchResultLimit.DefaultValue" />; a named one outside the accepted range is refused rather than clamped.</remarks>
    public int? ResultLimit { get; init; }
}
