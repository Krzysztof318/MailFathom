// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;

namespace MailFathom.Application.Emails.BrowseTimeline;

/// <summary>What a screen asks for when it draws one page of a message list.</summary>
/// <remarks>
/// <para>
/// This is the unvalidated contract, exactly as <see cref="ListEmails.ListEmailsRequest" /> is for the tool listing:
/// nothing here has been bounded, normalized, or checked against the accounts this deployment serves, and
/// <see cref="MailTimelineBrowser" /> is what does that.
/// </para>
/// <para>
/// The filters are the ones a list offers as controls a person can see — the folder, the unread and flagged toggles,
/// whether there is an attachment, and the range a jump to a date lands in. The ones the tool listing carries and this
/// does not are the ones a person types rather than toggles: a sender, a subject fragment, a keyword. Those are search,
/// and search is a module of its own that ranks rather than orders.
/// </para>
/// </remarks>
public sealed record BrowseTimelineRequest
{
    /// <summary>Gets the accounts to draw from, or empty for every account the caller's owner owns.</summary>
    public IReadOnlyList<MailAccountSelector> Accounts { get; init; } = [];

    /// <summary>Gets the folders to draw from, or empty for every folder of those accounts.</summary>
    public IReadOnlyList<MailFolderReference> Folders { get; init; } = [];

    /// <summary>Gets whether the account's junk folder is drawn too, which it is not unless the screen asks.</summary>
    public bool IncludeJunkMail { get; init; }

    /// <summary>Gets the remote <c>\Seen</c> state to require, or <see langword="null" /> for either.</summary>
    public bool? IsRemotelySeen { get; init; }

    /// <summary>Gets the remote <c>\Flagged</c> state to require, or <see langword="null" /> for either.</summary>
    public bool? IsRemotelyFlagged { get; init; }

    /// <summary>Gets whether attachments are required, or <see langword="null" /> for either.</summary>
    public bool? HasAttachments { get; init; }

    /// <summary>Gets the inclusive start of the received range, or <see langword="null" /> for no start.</summary>
    public DateTimeOffset? ReceivedOnOrAfter { get; init; }

    /// <summary>Gets the exclusive end of the received range, or <see langword="null" /> for no end.</summary>
    public DateTimeOffset? ReceivedBefore { get; init; }

    /// <summary>Gets the end of the timeline the list is sorted from.</summary>
    public EmailTimelineDirection Order { get; init; } = EmailTimelineDirection.NewestFirst;

    /// <summary>Gets whether the page asked for lies after the cursor in that order or before it.</summary>
    /// <remarks>A backward page continues from a cursor and from nothing else, so asking for one without a cursor is refused rather than answered with the first page.</remarks>
    public TimelinePageDirection PageDirection { get; init; } = TimelinePageDirection.Forward;

    /// <summary>Gets how many rows one page returns, or <see langword="null" /> to take the default.</summary>
    /// <remarks>An absent page size takes <see cref="MailboxQueryPageSize.DefaultValue" />; a named one outside the accepted range is refused rather than clamped.</remarks>
    public int? PageSize { get; init; }

    /// <summary>Gets the cursor a previous page returned, or <see langword="null" /> to read the leading end of the list.</summary>
    /// <remarks>The cursor is opaque and belongs to the filters and the order it was issued for; presenting it against a different list is refused.</remarks>
    public string? Cursor { get; init; }
}
