// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Domain.Accounts;
using MailMcp.Domain.Emails;
using MailMcp.Domain.Folders;

namespace MailMcp.Application.Emails.ListEmails;

/// <summary>What a caller asks for when listing emails from the local mailbox copy.</summary>
/// <remarks>
/// <para>
/// This is the unvalidated contract: every value is what a caller supplied, and nothing here has been normalized,
/// bounded, or checked against the accounts this deployment serves. <see cref="MailboxTimelineReader" /> does that and
/// produces the validated <see cref="EmailTimelineFilter" /> the read port receives, so no protocol adapter can reach a
/// query with a filter that skipped validation.
/// </para>
/// <para>
/// Accounts and folders are named by their domain identities rather than as text, so an adapter converts a caller's
/// strings once, at its own boundary, and a malformed identifier is refused before it reaches a use case.
/// </para>
/// </remarks>
public sealed record ListEmailsRequest
{
    /// <summary>Gets the accounts to list from, or empty for every account this deployment serves.</summary>
    public IReadOnlyList<MailAccountId> AccountIds { get; init; } = [];

    /// <summary>Gets the folder aliases to list from, or empty for every folder of the named accounts.</summary>
    public IReadOnlyList<MailFolderAlias> FolderAliases { get; init; } = [];

    /// <summary>Gets the address the sender must carry, in any case, or <see langword="null" /> for any sender.</summary>
    public string? SenderAddress { get; init; }

    /// <summary>Gets the address a <c>To</c> or <c>Cc</c> recipient must carry, or <see langword="null" /> for any recipient.</summary>
    public string? RecipientAddress { get; init; }

    /// <summary>Gets the fragment the subject must contain, compared without regard to case, or <see langword="null" /> for any subject.</summary>
    public string? SubjectFragment { get; init; }

    /// <summary>Gets the inclusive start of the received range, or <see langword="null" /> for no start.</summary>
    public DateTimeOffset? ReceivedOnOrAfter { get; init; }

    /// <summary>Gets the exclusive end of the received range, or <see langword="null" /> for no end.</summary>
    public DateTimeOffset? ReceivedBefore { get; init; }

    /// <summary>Gets the remote <c>\Seen</c> state to require, or <see langword="null" /> for either.</summary>
    public bool? IsRemotelySeen { get; init; }

    /// <summary>Gets whether attachments are required, or <see langword="null" /> for either.</summary>
    public bool? HasAttachments { get; init; }

    /// <summary>Gets the end of the timeline to read from.</summary>
    public EmailTimelineDirection Direction { get; init; } = EmailTimelineDirection.NewestFirst;

    /// <summary>Gets how many emails one page returns, or <see langword="null" /> to take the default.</summary>
    /// <remarks>An absent page size takes <see cref="MailboxQueryPageSize.DefaultValue" />; a named one outside the accepted range is refused rather than clamped.</remarks>
    public int? PageSize { get; init; }

    /// <summary>Gets the cursor a previous page returned, or <see langword="null" /> to read the first page.</summary>
    /// <remarks>The cursor is opaque and belongs to the filters it was issued for; presenting it against different filters is refused.</remarks>
    public string? Cursor { get; init; }
}
