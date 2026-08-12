// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Spam.Actions;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Spam;

/// <summary>Reads where one stored occurrence is and whether its server already reports it read.</summary>
/// <remarks>
/// A bounded projection rather than the entity, for the reason every read model here is one: acting on a verdict needs
/// the occurrence and one flag, and loading the row would put a subject and a set of addresses into memory to answer a
/// question about a folder.
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class SpamActionOccurrenceReader(MailFathomDbContext dbContext) : ISpamActionOccurrenceReader
{
    /// <inheritdoc />
    /// <remarks>
    /// A tombstoned occurrence is deliberately still readable, exactly as it is for classification. Whether it is acted
    /// on is settled above this: the mail server no longer holds the message, so the convergence pass finds nothing to
    /// move and the record ends as the failure it is rather than as a change nobody can explain.
    /// </remarks>
    public async Task<SpamActionOccurrence?> FindAsync(StoredEmailId emailId, CancellationToken cancellationToken)
    {
        var storedEmailId = emailId.Value;
        var row = await dbContext.StoredEmails
            .AsNoTracking()
            .Where(email => email.Id == storedEmailId)
            .Select(email => new
            {
                email.MailboxAccountId,
                email.MailFolder.Alias,
                email.MailFolder.ResolutionGeneration,
                email.UidValidity,
                email.Uid,
                email.IsRemotelySeen,
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            return null;
        }

        var folderAlias = MailFolderAlias.Create(row.Alias);

        return new SpamActionOccurrence(
            emailId,
            EmailOccurrenceId.Create(
                MailAccountId.Create(row.MailboxAccountId),
                new MailFolderResolutionId(
                    folderAlias,
                    MailFolderResolutionGeneration.Create(row.ResolutionGeneration)),
                ImapUidValidity.Create(row.UidValidity),
                ImapUid.Create(row.Uid)),
            folderAlias,
            row.IsRemotelySeen);
    }
}
