// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Emails.Threads;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Emails.Threads;

/// <summary>Reads one conversation's messages out of PostgreSQL.</summary>
/// <remarks>
/// A projection rather than an entity load, which is the same privacy control every other mailbox read applies: the
/// query names the columns a thread publishes, so nothing here can reach the stored raw MIME, and no row enters the
/// change tracker on a path that only reads.
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class StoredEmailThreadReader(MailFathomDbContext dbContext) : IEmailThreadReader
{
    /// <summary>How many merges one identifier is followed through before the chain is treated as unusable.</summary>
    /// <remarks>
    /// A merge points straight at the survivor, so a chain forms only when a survivor is itself merged into a thread
    /// older still — which needs the older thread to have been unreachable until a third message named both. That is
    /// rare and shallow. The ceiling is against a chain that reached the database some other way, where following it
    /// forever would hang a protocol call rather than answer it.
    /// </remarks>
    private const int MaximumMergeChainWalk = 64;

    /// <inheritdoc />
    public async Task<IReadOnlyList<ThreadedEmailSummary>> ReadEmailsAsync(
        EmailThreadId threadId,
        MailboxScope scope,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);

        if (await this.SurvivingThreadAsync(threadId.Value, cancellationToken) is not { } surviving)
        {
            return [];
        }

        // One row past the bound, because the count alone cannot tell a conversation that ends at the bound from one
        // that was cut there, and the caller states which of the two it is to whoever reads the thread.
        var rows = await this.Readable(surviving, scope)
            .OrderBy(email => email.Id)
            .Take(IEmailThreadReader.MaximumAssembledEmails + 1)
            .Select(email => new
            {
                email.Id,
                email.MailboxAccountId,
                FolderAlias = email.MailFolder.Alias,
                email.ParentStoredEmailId,
                email.Subject,
                email.SentAt,
                email.SenderAddress,
                email.SenderDisplayName,
            })
            .ToArrayAsync(cancellationToken);

        return
        [
            .. rows.Select(row => new ThreadedEmailSummary
            {
                StoredEmailId = StoredEmailId.Create(row.Id),
                AccountId = MailAccountId.Create(row.MailboxAccountId),
                FolderAlias = MailFolderAlias.Create(row.FolderAlias),
                ParentStoredEmailId = row.ParentStoredEmailId is { } parent
                    ? StoredEmailId.Create(parent)
                    : null,
                Subject = row.Subject,
                SentAt = row.SentAt,
                SenderAddress = row.SenderAddress,
                SenderDisplayName = row.SenderDisplayName,
            }),
        ];
    }

    /// <summary>Narrows one conversation's messages to the mail the scope admits.</summary>
    /// <remarks>
    /// <para>
    /// The scope is applied to the query rather than to the rows it returned, because the bound is on that query: a
    /// withheld message that consumed one of the bounded rows would push a readable one out of a conversation the
    /// caller is entitled to all of.
    /// </para>
    /// <para>
    /// It composes the narrowing every mail-returning read composes and states none of its own, which is what stops a
    /// caller's entitlement being read twice and read differently. What it does not compose is
    /// <see cref="StoredEmailSelectionPredicate.Matching" />: a conversation is read by membership rather than by
    /// filters, so narrowing it by the folder somebody happened to be listing would cut the thread.
    /// </para>
    /// <para>
    /// It is a member of this class rather than an expression inside the asynchronous read above, and that is
    /// load-bearing: a call made only inside an async method body belongs to the compiler-generated state machine, and
    /// the architecture rule holding every mail-returning read to this narrowing reads the class.
    /// </para>
    /// </remarks>
    private IQueryable<StoredEmailEntity> Readable(Guid survivingThreadId, MailboxScope scope) =>
        StoredEmailSelectionPredicate.WithinScope(
            dbContext.StoredEmails
                .AsNoTracking()
                .Where(email => email.EmailThreadId == survivingThreadId),
            scope);

    /// <summary>Follows a merged conversation to the one it was folded into, or reports that nothing holds it.</summary>
    /// <remarks>
    /// The walk is what makes an identifier a tool published before a merge keep working. Every message of a merged
    /// thread was repointed at the survivor when the merge happened, so this is only ever needed for the identifier
    /// itself rather than for finding the membership.
    /// </remarks>
    private async Task<Guid?> SurvivingThreadAsync(Guid threadId, CancellationToken cancellationToken)
    {
        var visited = new HashSet<Guid>();
        var candidate = (Guid?)threadId;

        for (var step = 0; step < MaximumMergeChainWalk && candidate is { } current && visited.Add(current); step++)
        {
            var merged = await dbContext.EmailThreads
                .AsNoTracking()
                .Where(thread => thread.Id == current)
                .Select(thread => new { thread.MergedIntoEmailThreadId })
                .SingleOrDefaultAsync(cancellationToken);

            if (merged is null)
            {
                return null;
            }

            if (merged.MergedIntoEmailThreadId is not { } survivor)
            {
                return current;
            }

            candidate = survivor;
        }

        return null;
    }
}
