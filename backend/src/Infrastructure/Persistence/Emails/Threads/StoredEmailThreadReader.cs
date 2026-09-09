// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
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
    /// <inheritdoc />
    public async Task<IReadOnlyList<ThreadedEmailSummary>> ReadEmailsAsync(
        EmailThreadId threadId,
        MailboxScope scope,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);

        if (await SurvivingEmailThread.ResolveAsync(dbContext, threadId.Value, cancellationToken) is not { } surviving)
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

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<EmailThreadId, int>> ReadMessageCountsAsync(
        IReadOnlyList<EmailThreadId> threadIds,
        MailboxScope scope,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(threadIds);
        ArgumentNullException.ThrowIfNull(scope);

        if (threadIds.Count is 0)
        {
            return new Dictionary<EmailThreadId, int>();
        }

        var identities = threadIds.Select(static threadId => threadId.Value).Distinct().ToArray();

        var rows = await Counted(dbContext.StoredEmails.AsNoTracking(), identities, scope)
            .ToArrayAsync(cancellationToken);

        return rows.ToDictionary(
            static row => EmailThreadId.Create(row.EmailThreadId),
            static row => row.MessageCount);
    }

    /// <summary>Counts the messages of the named conversations that the scope admits, grouped by conversation.</summary>
    /// <param name="emails">The stored emails to count over, untracked because nothing here writes.</param>
    /// <param name="threadIds">The conversations to count, already deduplicated.</param>
    /// <param name="scope">The accounts and folders configuration admits.</param>
    /// <returns>One row per conversation the scope admits any message of.</returns>
    /// <remarks>
    /// <para>
    /// The scope narrows the query for the reason it narrows the assembly beside it: a message in a folder an operator
    /// withheld is in no conversation this surface publishes, so counting it would report that folder's contents one
    /// integer at a time.
    /// </para>
    /// <para>
    /// It is a member of this class rather than an expression inside the asynchronous read above, and that is
    /// load-bearing for the reason <see cref="Readable" /> is: the architecture rule holding every mail-returning read
    /// to the shared narrowing reads the class, and a call made only inside an async method body belongs to the
    /// compiler-generated state machine instead. It takes the query rather than reaching for the context so the command
    /// it generates can be read without a database, which is the only place the grouping and the narrowing are visible.
    /// </para>
    /// </remarks>
    internal static IQueryable<StoredEmailThreadSizeRow> Counted(
        IQueryable<StoredEmailEntity> emails,
        Guid[] threadIds,
        MailboxScope scope) =>
        StoredEmailSelectionPredicate.WithinScope(
                emails.Where(email => email.EmailThreadId != null && threadIds.Contains(email.EmailThreadId.Value)),
                scope)
            .GroupBy(email => email.EmailThreadId!.Value)
            .Select(conversation => new StoredEmailThreadSizeRow(conversation.Key, conversation.Count()));

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
}
