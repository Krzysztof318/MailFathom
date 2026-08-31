// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Linq.Expressions;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Infrastructure.Persistence.Entities;

namespace MailFathom.Infrastructure.Persistence.Emails;

/// <summary>The columns a walk reads to name one stored email and the remote occurrence it came from.</summary>
/// <param name="Id">The stable local identity of the email.</param>
/// <param name="OwnerId">The owner the email belongs to, which decides what a re-reading of it redacts.</param>
/// <param name="MailboxAccountId">The configured account the email's folder belongs to.</param>
/// <param name="Alias">The configured alias of that folder.</param>
/// <param name="ResolutionGeneration">The generation the folder alias resolved in.</param>
/// <param name="UidValidity">The UID space the occurrence was read in.</param>
/// <param name="Uid">The UID the occurrence carried in that space.</param>
/// <remarks>
/// The projection stops here rather than constructing the occurrence identity directly, because a domain value object's
/// factory inside an <c>IQueryable</c> projection is either untranslatable or silently evaluated on the client. Both
/// backfill walks that resume by identity read exactly these columns, so the projection and the rebuild are one shape
/// rather than one per walk.
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed record StoredEmailOccurrenceRow(
    Guid Id,
    Guid OwnerId,
    string MailboxAccountId,
    string Alias,
    int ResolutionGeneration,
    uint UidValidity,
    uint Uid)
{
    /// <summary>Gets the projection every walk over outstanding occurrences selects.</summary>
    public static Expression<Func<StoredEmailEntity, StoredEmailOccurrenceRow>> Projection { get; } = email =>
        new StoredEmailOccurrenceRow(
            email.Id,
            email.OwnerId,
            email.MailFolder.MailboxAccountId,
            email.MailFolder.Alias,
            email.MailFolder.ResolutionGeneration,
            email.UidValidity,
            email.Uid);

    /// <summary>Rebuilds the remote occurrence identity the returned columns describe.</summary>
    /// <returns>The occurrence the row came from.</returns>
    public EmailOccurrenceId ToOccurrenceId() => EmailOccurrenceId.Create(
        MailAccountId.Create(this.MailboxAccountId),
        new MailFolderResolutionId(
            MailFolderAlias.Create(this.Alias),
            MailFolderResolutionGeneration.Create(this.ResolutionGeneration)),
        ImapUidValidity.Create(this.UidValidity),
        ImapUid.Create(this.Uid));
}
