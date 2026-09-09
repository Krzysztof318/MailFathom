// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Emails.ThreadStates;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Emails;
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
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class StoredThreadStateReader(MailFathomDbContext dbContext) : IStoredThreadStateReader
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
            stored.DerivedAt);
    }

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
