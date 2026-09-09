// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.ThreadStates;
using MailFathom.Application.Folders;
using MailFathom.Application.Persistence;
using MailFathom.Application.Spam.Gating;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Infrastructure.Persistence.Emails;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.Infrastructure.Persistence.Sessions;
using MailFathom.Infrastructure.Persistence.Spam;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.ThreadStates;

/// <summary>EF Core state for the derivation that says where each of an account's conversations stands.</summary>
/// <remarks>
/// <para>
/// The selection carries the arrival pipeline's own orderings and one comparison of its own. A message counts towards a
/// conversation once the rules have finished with it, it is settled where it is, the classification gate admits it, its
/// folder is one an operator asked to have derived from, and extraction has read a body out of it. What then decides
/// whether the conversation is read at all is the comparison between the shape it has now and the shape the stored
/// state was derived from.
/// </para>
/// <para>
/// A message whose body extraction could not read is in no count and in no turn. That keeps the two exactly consistent
/// — a conversation is counted by the messages a derivation would be shown — and it is self-correcting: a message whose
/// body arrives on a later run changes the count, and the conversation is derived again.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class StoredThreadStateStore(
    MailFathomDbContext dbContext,
    IMailFolderParticipationReader folderParticipation,
    DerivedWorkGate derivedWorkGate)
    : IStoredThreadStateStore
{
    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// Two queries rather than one. The first groups the account's mail by conversation and keeps the conversations
    /// whose stored state does not describe the shape they now have; the second reads the messages of the ones inside
    /// the bound. Reading the text in the grouped query is not possible — a group is an aggregate — and reading it for
    /// every conversation would fetch the text of the ones this pass will not derive from.
    /// </para>
    /// <para>
    /// Ordering is by the conversation's identifier, which is total, stable, and already indexed. No resume position
    /// travels with the batch, because writing a state against the shape the conversation now has is what takes it out
    /// of the first query.
    /// </para>
    /// <para>
    /// The messages are ordered by when they were written and then by identity, which is the reading order rather than
    /// the reply relation the thread screen walks. A derivation is being shown a conversation to say where it stands,
    /// and what it needs is the order the people in it experienced; rebuilding the reply tree here would be a second
    /// implementation of the screen's own walk for an answer that would not change.
    /// </para>
    /// </remarks>
    public async Task<IReadOnlyList<DerivableThread>> GetThreadsAwaitingStateAsync(
        MailAccountIdentity account,
        int batchSize,
        int maximumMessagesPerThread,
        int maximumCharactersPerMessage,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumMessagesPerThread);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumCharactersPerMessage);

        var userId = account.User.Value;
        var mailboxAccountId = account.Id.Value;
        var terms = derivedWorkGate.ReadTerms();

        var counted = Selecting(
            dbContext.StoredEmails.AsNoTracking(),
            userId,
            mailboxAccountId,
            folderParticipation.FoldersGeneratingEmbeddings,
            terms);

        var awaiting = await Awaiting(counted, dbContext.EmailThreadStates.AsNoTracking())
            .OrderBy(conversation => conversation.EmailThreadId)
            .Take(batchSize)
            .ToArrayAsync(cancellationToken);

        if (awaiting.Length is 0)
        {
            return [];
        }

        var readable = awaiting
            .Where(conversation => conversation.MessageCount <= maximumMessagesPerThread)
            .Select(static conversation => conversation.EmailThreadId)
            .ToArray();

        var messages = readable.Length is 0
            ? []
            : await Selecting(
                    dbContext.StoredEmails.AsNoTracking(),
                    userId,
                    mailboxAccountId,
                    folderParticipation.FoldersGeneratingEmbeddings,
                    terms)
                .Where(email => readable.Contains(email.EmailThreadId!.Value))
                .OrderBy(email => email.SentAt)
                .ThenBy(email => email.Id)
                .Select(email => new DerivableThreadMessageRow(
                    email.EmailThreadId!.Value,
                    email.Id,
                    email.Subject,
                    email.SenderDisplayName,
                    email.SentAt,
                    email.SearchDocument!.BodyText!.Substring(0, maximumCharactersPerMessage)))
                .ToArrayAsync(cancellationToken);

        var byThread = messages
            .GroupBy(static row => row.EmailThreadId)
            .ToDictionary(static wrote => wrote.Key, static wrote => wrote.ToArray());

        return [.. awaiting.Select(conversation => Compose(conversation, byThread, maximumMessagesPerThread))];
    }

    /// <inheritdoc />
    /// <remarks>
    /// The record is written whole rather than merged: a derivation is one answer about one conversation, so a second
    /// one replaces the first's statements instead of adding to them. Loading the existing statements is what makes
    /// that a delete and an insert EF Core can stage, rather than a write that would leave a superseded sentence beside
    /// the current one.
    /// </remarks>
    public async Task SaveAsync(IPersistenceSession session, EmailThreadState state, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);

        var context = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);
        var emailThreadId = state.ThreadId.Value;

        var existing = await context.EmailThreadStates
            .Include(record => record.Entries)
            .FirstOrDefaultAsync(record => record.EmailThreadId == emailThreadId, cancellationToken);

        if (existing is null)
        {
            existing = new EmailThreadStateEntity { EmailThreadId = emailThreadId };
            context.EmailThreadStates.Add(existing);
        }
        else
        {
            context.EmailThreadStateEntries.RemoveRange(existing.Entries);
            existing.Entries.Clear();
        }

        existing.Coverage = state.Coverage;
        existing.DerivedAt = state.DerivedAt;
        existing.DerivedFromMessageCount = state.DerivedFrom.MessageCount;
        existing.DerivedFromLatestArrival = state.DerivedFrom.LatestArrival;

        var ordinals = new Dictionary<ThreadStateAspect, int>();

        foreach (var entry in state.Entries)
        {
            ordinals.TryGetValue(entry.Aspect, out var ordinal);
            ordinals[entry.Aspect] = ordinal + 1;

            existing.Entries.Add(new EmailThreadStateEntryEntity
            {
                EmailThreadId = emailThreadId,
                Aspect = entry.Aspect,
                Ordinal = ordinal,
                Text = entry.Text,
                OwedBy = entry.OwedBy,
                DueAt = entry.DueAt,
                Sources = [.. entry.Sources.Select(static source => source.Value)],
            });
        }
    }

    /// <summary>Narrows stored mail to the messages a conversation's state is derived from.</summary>
    /// <param name="emails">The emails to narrow.</param>
    /// <param name="userId">The user whose account this pass belongs to, which is what the index leads with.</param>
    /// <param name="mailboxAccountId">The configured account this pass belongs to.</param>
    /// <param name="embeddedFolders">The folders a mapping admits to derived work.</param>
    /// <param name="terms">The classification terms the whole batch is decided under.</param>
    /// <returns>The narrowed query, which PostgreSQL evaluates in full.</returns>
    /// <remarks>
    /// Written as a composable predicate rather than inline, so what counts towards a conversation is one statement
    /// that can be asserted about directly — and so that the count and the messages a derivation is shown are read
    /// through the same clause rather than through two that could drift apart.
    /// </remarks>
    internal static IQueryable<StoredEmailEntity> Selecting(
        IQueryable<StoredEmailEntity> emails,
        Guid userId,
        string mailboxAccountId,
        IReadOnlyList<MailFolderIdentity> embeddedFolders,
        DerivedWorkAdmissionTerms terms) => DerivedWorkAdmittedEmails.Admitting(
        AccountScopedMailFolders.Admitting(
            emails
                .Where(StoredEmailTombstone.IsNotTombstoned)
                .Where(email => email.UserId == userId
                    && email.MailboxAccountId == mailboxAccountId
                    && email.EmailThreadId != null
                    && email.SearchDocument != null
                    && email.SearchDocument.BodyText != null)
                .Where(MailAwaitingRuleEvaluation.IsFinishedWith)
                .Where(MailAwaitingRelocation.IsSettledWhereItIs),
            embeddedFolders),
        terms);

    /// <summary>Groups the narrowed mail by conversation and keeps the ones no stored state currently describes.</summary>
    /// <param name="selected">The narrowed mail of one account.</param>
    /// <param name="states">The states already stored, untracked because nothing here writes.</param>
    /// <returns>One row per conversation awaiting a state, carrying the shape it has now.</returns>
    /// <remarks>
    /// A left join rather than two round trips, and the comparison is the whole selection: a conversation with no state
    /// has never been derived, and one whose state was derived from a different count or a different newest arrival has
    /// changed since. Writing the state against the shape read here is what takes the conversation back out.
    /// </remarks>
    internal static IQueryable<ThreadAwaitingStateRow> Awaiting(
        IQueryable<StoredEmailEntity> selected,
        IQueryable<EmailThreadStateEntity> states) =>
        from conversation in selected
            .GroupBy(email => email.EmailThreadId!.Value)
            .Select(wrote => new
            {
                EmailThreadId = wrote.Key,
                MessageCount = wrote.Count(),
                LatestArrival = wrote.Max(email => email.ReceivedAt),
            })
        join stored in states on conversation.EmailThreadId equals stored.EmailThreadId into found
        from stored in found.DefaultIfEmpty()
        where stored == null
            || stored.DerivedFromMessageCount != conversation.MessageCount
            || stored.DerivedFromLatestArrival != conversation.LatestArrival
        select new ThreadAwaitingStateRow(
            conversation.EmailThreadId,
            conversation.MessageCount,
            conversation.LatestArrival);

    /// <summary>Puts one selected conversation together with the messages that were read for it.</summary>
    /// <remarks>
    /// A conversation past the bound is composed with no messages at all, which is what makes the answer honest: its
    /// text was never read out of the store, so there is nothing partial to send by mistake and the record written for
    /// it says the conversation was not read rather than summarizing the part that would have fitted.
    /// </remarks>
    private static DerivableThread Compose(
        ThreadAwaitingStateRow conversation,
        IReadOnlyDictionary<Guid, DerivableThreadMessageRow[]> byThread,
        int maximumMessagesPerThread)
    {
        var revision = new ThreadStateRevision(conversation.MessageCount, conversation.LatestArrival);

        if (conversation.MessageCount > maximumMessagesPerThread
            || !byThread.TryGetValue(conversation.EmailThreadId, out var rows))
        {
            return new DerivableThread(
                EmailThreadId.Create(conversation.EmailThreadId),
                Subject: null,
                revision,
                Messages: [],
                ExceedsBound: conversation.MessageCount > maximumMessagesPerThread);
        }

        return new DerivableThread(
            EmailThreadId.Create(conversation.EmailThreadId),
            rows[0].Subject,
            revision,
            [
                .. rows.Select(static (row, position) => new DerivableThreadMessage(
                    StoredEmailId.Create(row.StoredEmailId),
                    position,
                    row.SenderDisplayName,
                    row.SentAt,
                    row.Text)),
            ],
            ExceedsBound: false);
    }

    private sealed record DerivableThreadMessageRow(
        Guid EmailThreadId,
        Guid StoredEmailId,
        string? Subject,
        string? SenderDisplayName,
        DateTimeOffset? SentAt,
        string Text);
}

/// <summary>One conversation whose stored state does not describe the shape it now has.</summary>
/// <param name="EmailThreadId">The conversation, which is always a surviving thread because merged mail is repointed.</param>
/// <param name="MessageCount">How many of its messages a derivation would be shown.</param>
/// <param name="LatestArrival">When the most recent of them arrived, or <see langword="null" /> where none recorded an arrival.</param>
internal sealed record ThreadAwaitingStateRow(Guid EmailThreadId, int MessageCount, DateTimeOffset? LatestArrival);
