// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.CodeCoverage;
using MailFathom.Domain.Delivery.Drafts;

namespace MailFathom.Infrastructure.Persistence.Entities;

/// <summary>One copy of a draft MailFathom put into the drafts folder, and what has become of it since.</summary>
/// <remarks>
/// <para>
/// One row per revision, which is what makes a replacement expressible at all: IMAP has no command that changes a
/// stored message, so a new version is a new message beside the old one and between the two commands the folder holds
/// two copies of the same draft.
/// </para>
/// <para>
/// The row exists from before the <c>APPEND</c> goes out, which is what stops a second copy: a process that died
/// between the command and the answer left a row saying the copy may be there, and nothing appends that revision again
/// on the strength of it.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class MailDraftCopyEntity
{
    /// <summary>The longest remote folder path stored, matching the bound the folder binding's own path column carries.</summary>
    internal const int MaximumFolderPathLength = 512;

    /// <summary>The longest <c>Message-ID</c> stored, matching the bound arriving mail's own column carries.</summary>
    internal const int MaximumInternetMessageIdLength = 998;

    public Guid MailDraftId { get; set; }

    /// <summary>Gets or sets which revision of the draft this copy carries, which completes the key.</summary>
    /// <remarks>
    /// Keying by the revision rather than by an identifier of its own is what makes the append idempotent without a
    /// read-then-write: a second attempt to append the same revision is refused by the database rather than by a check
    /// two callers can pass between, and a second copy in the owner's drafts folder is a draft they read as two.
    /// </remarks>
    public int Revision { get; set; }

    public MailDraftEntity? MailDraft { get; set; }

    /// <summary>Gets or sets MailFathom's own name for the folder the copy went into.</summary>
    public required string FolderAlias { get; set; }

    /// <summary>Gets or sets the remote path the copy was appended to, which a later attempt compares its resolution against.</summary>
    public required string FolderPath { get; set; }

    /// <summary>Gets or sets what has become of the copy.</summary>
    public MailDraftCopyStage Stage { get; set; }

    /// <summary>Gets or sets the UIDVALIDITY an <c>APPENDUID</c> response named, and <see langword="null" /> where the server named none.</summary>
    public uint? PlacementUidValidity { get; set; }

    /// <summary>Gets or sets the UID an <c>APPENDUID</c> response named, and <see langword="null" /> where the server named none.</summary>
    public uint? PlacementUid { get; set; }

    /// <summary>Gets or sets the <c>Message-ID</c> the appended bytes carry, which is what an operator finds a diverged copy by.</summary>
    public string? InternetMessageId { get; set; }

    /// <summary>Gets or sets when the append was issued.</summary>
    public DateTimeOffset AppendedAt { get; set; }

    /// <summary>Gets or sets when the copy stopped standing in the folder, and <see langword="null" /> while it still does.</summary>
    public DateTimeOffset? SettledAt { get; set; }

    /// <summary>Gets or sets PostgreSQL's <c>xmin</c> token, which makes settling this copy a conditional write.</summary>
    /// <remarks>
    /// The row carries one of its own rather than relying on the draft's, because a copy is confirmed and withdrawn
    /// without the draft above it changing: without a token here two passes settling one copy would be a silent
    /// last-writer-win, and what that decides is whether a message is left in somebody's folder.
    /// </remarks>
    public uint ConcurrencyVersion { get; set; }
}
