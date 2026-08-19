// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.CodeCoverage;
using MailFathom.Domain.Delivery.Filing;

namespace MailFathom.Infrastructure.Persistence.Entities;

/// <summary>One copy of an outgoing message MailFathom put into a folder of the mailbox.</summary>
/// <remarks>
/// A row exists from before the <c>APPEND</c> goes out, which is what stops a second copy: a process that died between
/// the command and the answer left a row saying the copy may be there, and nothing appends again on the strength of it.
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class OutgoingEmailFilingEntity
{
    /// <summary>The longest remote folder path stored, matching the bound the folder binding's own path column carries.</summary>
    internal const int MaximumFolderPathLength = 512;

    /// <summary>The longest <c>Message-ID</c> stored, matching the bound arriving mail's own column carries.</summary>
    internal const int MaximumInternetMessageIdLength = 998;

    public Guid OutgoingEmailId { get; set; }

    public OutgoingEmailEntity? OutgoingEmail { get; set; }

    /// <summary>Gets or sets the filing's own name, which is the identity the closed enumeration publishes.</summary>
    /// <remarks>
    /// The column holds the name directly rather than a converted enum, for the reason the mutation name does: the name
    /// is what the value object is, and it is the same word a log line and a counter dimension already carry.
    /// </remarks>
    public required string Filing { get; set; }

    /// <summary>
    /// Gets or sets the account whose mailbox holds the copy, copied from the record above because the query
    /// synchronization issues on every batch leads with it and an index cannot span a join.
    /// </summary>
    public required string MailboxAccountId { get; set; }

    /// <summary>Gets or sets MailFathom's own name for the folder the copy went into.</summary>
    public required string FolderAlias { get; set; }

    /// <summary>Gets or sets the remote path the copy was appended to, which is what a discovery is compared against.</summary>
    public required string FolderPath { get; set; }

    /// <summary>Gets or sets how far the append has durably got.</summary>
    public OutgoingMailFilingStage Stage { get; set; }

    /// <summary>Gets or sets the UIDVALIDITY an <c>APPENDUID</c> response named, and <see langword="null" /> where the server named none.</summary>
    public uint? PlacementUidValidity { get; set; }

    /// <summary>Gets or sets the UID an <c>APPENDUID</c> response named, and <see langword="null" /> where the server named none.</summary>
    public uint? PlacementUid { get; set; }

    /// <summary>Gets or sets the <c>Message-ID</c> the appended bytes carry, which is how a server naming no placement is joined.</summary>
    public string? InternetMessageId { get; set; }

    /// <summary>Gets or sets when the append was issued.</summary>
    public DateTimeOffset AppendedAt { get; set; }

    /// <summary>Gets or sets when synchronization met the copy, and <see langword="null" /> while it has not.</summary>
    public DateTimeOffset? ObservedAt { get; set; }

    /// <summary>Gets or sets when the copy was taken back out of the folder, and <see langword="null" /> while it stands.</summary>
    public DateTimeOffset? WithdrawnAt { get; set; }

    /// <summary>Gets or sets the PostgreSQL <c>xmin</c> system column this row's optimistic concurrency is decided on.</summary>
    public uint ConcurrencyVersion { get; set; }
}
