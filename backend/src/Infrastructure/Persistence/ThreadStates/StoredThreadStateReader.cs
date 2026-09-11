// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Emails.ThreadStates;
using MailFathom.Application.Folders;
using MailFathom.Application.Spam.Gating;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Infrastructure.Persistence.Emails;
using MailFathom.Infrastructure.Persistence.Emails.Threads;
using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.ThreadStates;

/// <summary>Reads one conversation's stored state out of PostgreSQL.</summary>
/// <remarks>
/// <para>
/// A projection rather than an entity load, which is the same privacy control every other mail-derived read applies:
/// the query names the columns a state publishes, and no row enters the change tracker on a path that only reads.
/// </para>
/// <para>
/// The state is published only where the caller may see the conversation it describes, and that is asked of the mail
/// rather than of the state: a conversation whose every message sits in a folder an operator withheld is one this
/// surface has no conversation for, so it has no state either. Answering otherwise would report a withheld folder's
/// contents one derived sentence at a time.
/// </para>
/// <para>
/// Whether the state still describes the conversation is asked of the mail as well, and in the derivation pass's own
/// terms — see <see cref="OwedADerivation" />. A stored state is written against the shape the conversation had, and
/// a reply that arrived since changes the conversation without changing the record, so the record alone cannot say.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class StoredThreadStateReader(
    MailFathomDbContext dbContext,
    IMailFolderParticipationReader folderParticipation,
    DerivedWorkGate derivedWorkGate)
    : IStoredThreadStateReader
{
    /// <inheritdoc />
    public async Task<EmailThreadState?> ReadStateAsync(
        EmailThreadId threadId,
        MailboxScope scope,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);

        if (await SurvivingEmailThread.ResolveAsync(dbContext, threadId.Value, cancellationToken) is not { } surviving)
        {
            return null;
        }

        if (!await this.Readable(surviving, scope).AnyAsync(cancellationToken))
        {
            return null;
        }

        var stored = await dbContext.EmailThreadStates
            .AsNoTracking()
            .Where(state => state.EmailThreadId == surviving)
            .Select(state => new StoredThreadStateRow(
                state.EmailThread!.UserId,
                state.EmailThread.MailboxAccountId,
                state.Coverage,
                state.DerivedAt,
                state.DerivedFromMessageCount,
                state.DerivedFromLatestArrival,
                state.Entries
                    .OrderBy(entry => entry.Aspect)
                    .ThenBy(entry => entry.Ordinal)
                    .Select(entry => new StoredThreadStateEntryRow(
                        entry.Aspect,
                        entry.Text,
                        entry.OwedBy,
                        entry.DueAt,
                        entry.Sources))
                    .ToList()))
            .SingleOrDefaultAsync(cancellationToken);

        if (stored is null)
        {
            return null;
        }

        var owed = await OwedADerivation(
                dbContext.StoredEmails.AsNoTracking(),
                dbContext.EmailThreadStates.AsNoTracking(),
                surviving,
                stored.UserId,
                stored.MailboxAccountId,
                folderParticipation.FoldersGeneratingEmbeddings,
                derivedWorkGate.ReadTerms())
            .AnyAsync(cancellationToken);

        return new EmailThreadState(
            EmailThreadId.Create(surviving),
            stored.Coverage,
            [
                .. stored.Entries.Select(static entry => ThreadStateEntry.Create(
                    entry.Aspect,
                    entry.Text,
                    [.. entry.Sources.Select(StoredEmailId.Create)],
                    entry.OwedBy,
                    entry.DueAt)),
            ],
            new ThreadStateRevision(stored.DerivedFromMessageCount, stored.DerivedFromLatestArrival),
            stored.DerivedAt,
            IsCurrent: !owed);
    }

    /// <summary>Asks the derivation pass's own question of one conversation: whether it is owed a derivation now.</summary>
    /// <param name="emails">The stored mail to count the conversation from.</param>
    /// <param name="states">The states already stored.</param>
    /// <param name="survivingThreadId">The conversation, as the surviving thread of any merge.</param>
    /// <param name="userId">The user whose account the conversation belongs to.</param>
    /// <param name="mailboxAccountId">The configured account the conversation belongs to.</param>
    /// <param name="embeddedFolders">The folders a mapping admits to derived work.</param>
    /// <param name="terms">The classification terms in force.</param>
    /// <returns>One row where the stored state no longer describes the conversation, and none where it still does.</returns>
    /// <remarks>
    /// The selection and the comparison are the store's, composed rather than restated, so a read calls a state current
    /// exactly when the next pass would leave the conversation alone. A second comparison written here would be a second
    /// opinion about the same shape, and the day the two disagreed a reader would be shown as current a state the pass
    /// had already queued to replace — or be told a state was stale that no pass would ever derive again.
    /// </remarks>
    internal static IQueryable<ThreadAwaitingStateRow> OwedADerivation(
        IQueryable<StoredEmailEntity> emails,
        IQueryable<EmailThreadStateEntity> states,
        Guid survivingThreadId,
        Guid userId,
        string mailboxAccountId,
        IReadOnlyList<MailFolderIdentity> embeddedFolders,
        DerivedWorkAdmissionTerms terms) =>
        StoredThreadStateStore.Awaiting(
            StoredThreadStateStore.Selecting(
                emails.Where(email => email.EmailThreadId == survivingThreadId),
                userId,
                mailboxAccountId,
                embeddedFolders,
                terms),
            states.Where(state => state.EmailThreadId == survivingThreadId));

    /// <summary>Narrows one conversation's messages to the mail the scope admits.</summary>
    /// <remarks>
    /// It composes the narrowing every mail-returning read composes and states none of its own, which is what stops a
    /// caller's entitlement being read twice and read differently. It is a member of this class rather than an
    /// expression inside the asynchronous read above, and that is load-bearing for the reason the thread reader's own
    /// is: a call made only inside an async method body belongs to the compiler-generated state machine, and the
    /// architecture rule holding every mail-derived read to this narrowing reads the class.
    /// </remarks>
    private IQueryable<StoredEmailEntity> Readable(Guid survivingThreadId, MailboxScope scope) =>
        StoredEmailSelectionPredicate.WithinScope(
            dbContext.StoredEmails
                .AsNoTracking()
                .Where(email => email.EmailThreadId == survivingThreadId),
            scope);

    private sealed record StoredThreadStateRow(
        Guid UserId,
        string MailboxAccountId,
        ThreadStateCoverage Coverage,
        DateTimeOffset DerivedAt,
        int DerivedFromMessageCount,
        DateTimeOffset? DerivedFromLatestArrival,
        IReadOnlyList<StoredThreadStateEntryRow> Entries);

    private sealed record StoredThreadStateEntryRow(
        ThreadStateAspect Aspect,
        string Text,
        string? OwedBy,
        DateTimeOffset? DueAt,
        Guid[] Sources);
}
