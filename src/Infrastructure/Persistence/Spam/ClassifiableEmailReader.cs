// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Spam;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Spam;

/// <summary>Reads the account and folder of one stored occurrence, and nothing else about it.</summary>
/// <remarks>
/// A bounded projection rather than the entity, for the reason every read model here is one: classification needs three
/// values, and loading a whole email row would put its subject and its participants into memory to answer a question
/// about where it lives.
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class ClassifiableEmailReader(MailFathomDbContext dbContext) : IClassifiableEmailReader
{
    /// <inheritdoc />
    /// <remarks>
    /// A tombstoned occurrence is deliberately still readable here. The message left the server, and whether the local
    /// copy is kept is a disposition an owner chose; a copy that is kept is mail a reader can still reach, so refusing to
    /// classify it would leave exactly the mail nobody else can act on unclassified.
    /// </remarks>
    public async Task<ClassifiableEmail?> FindAsync(StoredEmailId emailId, CancellationToken cancellationToken)
    {
        var storedEmailId = emailId.Value;
        var row = await dbContext.StoredEmails
            .AsNoTracking()
            .Where(email => email.Id == storedEmailId)
            .Select(email => new { email.MailboxAccountId, email.MailFolder.Alias })
            .SingleOrDefaultAsync(cancellationToken);

        return row is null
            ? null
            : new ClassifiableEmail(
                emailId,
                MailAccountId.Create(row.MailboxAccountId),
                MailFolderAlias.Create(row.Alias));
    }

    /// <inheritdoc />
    /// <remarks>
    /// The folder is matched on the alias the mapping gave it rather than on the remote path, because the alias is what
    /// the run's scope was written in and what an operator typed. A tombstoned occurrence is walked for the reason it is
    /// readable one at a time: the local copy is mail somebody can still reach, so leaving it out would put exactly the
    /// mail nobody else can act on outside every run.
    /// </remarks>
    public async Task<IReadOnlyList<ClassifiableEmail>> GetStoredEmailsAsync(
        MailAccountId accountId,
        IReadOnlyList<MailFolderAlias> folderAliases,
        StoredEmailId? resumeAfter,
        int batchSize,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(folderAliases);
        ArgumentOutOfRangeException.ThrowIfLessThan(batchSize, 1);

        if (folderAliases.Count == 0)
        {
            return [];
        }

        var mailboxAccountId = accountId.Value;
        string[] aliases = [.. folderAliases.Select(static alias => alias.Value)];
        var emails = dbContext.StoredEmails
            .AsNoTracking()
            .Where(email => email.MailboxAccountId == mailboxAccountId && aliases.Contains(email.MailFolder.Alias));

        if (resumeAfter is { } position)
        {
            var boundary = position.Value;

            emails = emails.Where(email => email.Id > boundary);
        }

        var rows = await emails
            .OrderBy(email => email.Id)
            .Take(batchSize)
            .Select(email => new { email.Id, email.MailFolder.Alias })
            .ToArrayAsync(cancellationToken);

        return
        [
            .. rows.Select(row => new ClassifiableEmail(
                StoredEmailId.Create(row.Id),
                accountId,
                MailFolderAlias.Create(row.Alias))),
        ];
    }
}
