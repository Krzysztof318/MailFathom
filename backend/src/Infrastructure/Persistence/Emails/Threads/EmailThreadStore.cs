// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Threads;
using MailFathom.Application.Persistence;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.Infrastructure.Persistence.Sessions;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Emails.Threads;

/// <summary>EF Core state for the conversations an account's stored mail is assembled into.</summary>
/// <remarks>
/// <para>
/// Every method reads and writes through the caller's session, so a placement is committed with the message it is
/// about. Nothing here decides anything: which conversation a message belongs to, which conversations merge, and which
/// message answers which are all <see cref="EmailThreadAssembly" />'s, and this is what those decisions are made
/// against and recorded in.
/// </para>
/// <para>
/// Each read looks in the change tracker as well as in the database, because a LINQ query never sees a row the same
/// uncommitted session added. Both passes matter here: an arrival places a message that has not been inserted yet, and
/// a re-derivation batch places fifty messages of one conversation in one transaction, where every message after the
/// first is bound by rows only the tracker holds.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class EmailThreadStore(TimeProvider timeProvider) : IEmailThreadStore
{
    /// <inheritdoc />
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="identifiers" /> is <see langword="null" />.</exception>
    public async Task<IReadOnlyList<EmailThreadBinding>> FindBindingsAsync(
        IPersistenceSession session,
        MailAccountId accountId,
        IReadOnlyList<string> identifiers,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(identifiers);

        var dbContext = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);
        var account = accountId.Value;
        var digestedIdentifiers = identifiers
            .Distinct(StringComparer.Ordinal)
            .ToDictionary(EmailThreadIdentifierDigest.Of, identifier => identifier, StringComparer.Ordinal);
        var digests = digestedIdentifiers.Keys.ToArray();

        var persisted = await dbContext.EmailThreadIdentifiers
            .Where(binding => binding.MailboxAccountId == account && digests.Contains(binding.IdentifierHash))
            .ToListAsync(cancellationToken);

        var pending = dbContext.EmailThreadIdentifiers.Local
            .Where(binding => binding.MailboxAccountId == account && digests.Contains(binding.IdentifierHash));

        var bound = persisted
            .Concat(pending)
            .DistinctBy(binding => binding.IdentifierHash, StringComparer.Ordinal)
            .ToArray();

        var bindings = new List<EmailThreadBinding>(bound.Length);

        foreach (var binding in bound)
        {
            // Resolved one at a time through the primary key, which the change tracker answers without a query, so a
            // conversation this session started a moment ago is as visible as one the database already holds.
            var thread = await dbContext.EmailThreads.FindAsync([binding.EmailThreadId], cancellationToken);

            if (thread is not null)
            {
                bindings.Add(new EmailThreadBinding(
                    digestedIdentifiers[binding.IdentifierHash],
                    EmailThreadId.Create(thread.Id),
                    thread.AssembledAt));
            }
        }

        return bindings;
    }

    /// <inheritdoc />
    public async Task<EmailThreadId> StartThreadAsync(
        IPersistenceSession session,
        MailAccountId accountId,
        CancellationToken cancellationToken)
    {
        var dbContext = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);
        var assembledAt = timeProvider.GetUtcNow();
        var thread = new EmailThreadEntity
        {
            Id = Guid.CreateVersion7(assembledAt),
            MailboxAccountId = accountId.Value,
            AssembledAt = assembledAt,
        };

        dbContext.EmailThreads.Add(thread);

        return EmailThreadId.Create(thread.Id);
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="identifiers" /> is <see langword="null" />.</exception>
    public async Task BindIdentifiersAsync(
        IPersistenceSession session,
        MailAccountId accountId,
        IReadOnlyList<string> identifiers,
        EmailThreadId threadId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(identifiers);

        var dbContext = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);

        foreach (var identifier in identifiers)
        {
            dbContext.EmailThreadIdentifiers.Add(new EmailThreadIdentifierEntity
            {
                MailboxAccountId = accountId.Value,
                IdentifierHash = EmailThreadIdentifierDigest.Of(identifier),
                EmailThreadId = threadId.Value,
            });
        }
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="mergedThreadIds" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// The rows are repointed through the change tracker rather than by a set-based update, deliberately. A batched
    /// update runs straight at the connection and cannot see what the session has staged, so a re-derivation batch
    /// holding fifty tracked emails would write their stale conversation back over it at commit. Loading the rows costs
    /// a merge what the conversation is long, and a merge is the rare case rather than the ordinary one.
    /// </remarks>
    public async Task MergeThreadsAsync(
        IPersistenceSession session,
        IReadOnlyList<EmailThreadId> mergedThreadIds,
        EmailThreadId survivingThreadId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(mergedThreadIds);

        var dbContext = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);
        var merged = mergedThreadIds.Select(threadId => threadId.Value).ToArray();
        var surviving = survivingThreadId.Value;

        var emails = await dbContext.StoredEmails
            .Where(email => email.EmailThreadId != null && merged.Contains(email.EmailThreadId.Value))
            .ToListAsync(cancellationToken);

        foreach (var email in emails
                     .Concat(dbContext.StoredEmails.Local)
                     .Where(email => email.EmailThreadId is { } threadId && merged.Contains(threadId)))
        {
            email.EmailThreadId = surviving;
        }

        var bindings = await dbContext.EmailThreadIdentifiers
            .Where(binding => merged.Contains(binding.EmailThreadId))
            .ToListAsync(cancellationToken);

        foreach (var binding in bindings
                     .Concat(dbContext.EmailThreadIdentifiers.Local)
                     .Where(binding => merged.Contains(binding.EmailThreadId)))
        {
            binding.EmailThreadId = surviving;
        }

        foreach (var threadId in merged)
        {
            if (await dbContext.EmailThreads.FindAsync([threadId], cancellationToken) is { } thread)
            {
                thread.MergedIntoEmailThreadId = surviving;
            }
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ThreadedEmail>> FindByMessageIdentifierAsync(
        IPersistenceSession session,
        EmailThreadId threadId,
        string internetMessageId,
        CancellationToken cancellationToken)
    {
        var dbContext = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);
        var thread = threadId.Value;

        var persisted = await dbContext.StoredEmails
            .Where(email => email.EmailThreadId == thread && email.InternetMessageId == internetMessageId)
            .ToListAsync(cancellationToken);

        var pending = dbContext.StoredEmails.Local
            .Where(email => email.EmailThreadId == thread && email.InternetMessageId == internetMessageId);

        return Threaded(persisted.Concat(pending));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ThreadedEmail>> FindUnplacedAnswersAsync(
        IPersistenceSession session,
        EmailThreadId threadId,
        string answeredIdentifier,
        CancellationToken cancellationToken)
    {
        var dbContext = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);
        var thread = threadId.Value;

        var persisted = await dbContext.StoredEmails
            .Where(email => email.EmailThreadId == thread
                && email.InReplyTo == answeredIdentifier
                && email.ParentStoredEmailId == null)
            .ToListAsync(cancellationToken);

        var pending = dbContext.StoredEmails.Local
            .Where(email => email.EmailThreadId == thread
                && email.InReplyTo == answeredIdentifier
                && email.ParentStoredEmailId == null);

        return Threaded(persisted.Concat(pending));
    }

    /// <inheritdoc />
    public async Task<ThreadedEmail?> FindThreadedEmailAsync(
        IPersistenceSession session,
        StoredEmailId storedEmailId,
        CancellationToken cancellationToken)
    {
        var dbContext = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);
        var email = await dbContext.StoredEmails.FindAsync([storedEmailId.Value], cancellationToken);

        return email is null ? null : ThreadedEmails.Of(email);
    }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">Thrown when the email disappeared between its own write and this one.</exception>
    public async Task PlaceAsync(
        IPersistenceSession session,
        StoredEmailId storedEmailId,
        EmailThreadId threadId,
        StoredEmailId? answeredStoredEmailId,
        CancellationToken cancellationToken)
    {
        var email = await RequiredAsync(session, storedEmailId, cancellationToken);

        email.EmailThreadId = threadId.Value;
        email.ParentStoredEmailId = answeredStoredEmailId?.Value;
    }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">Thrown when the answering email disappeared before this write.</exception>
    public async Task LinkAnswerAsync(
        IPersistenceSession session,
        StoredEmailId answerStoredEmailId,
        StoredEmailId answeredStoredEmailId,
        CancellationToken cancellationToken)
    {
        var answer = await RequiredAsync(session, answerStoredEmailId, cancellationToken);

        answer.ParentStoredEmailId = answeredStoredEmailId.Value;
    }

    /// <summary>Reads the row a write is about, which the change tracker answers for one this session staged.</summary>
    private static async Task<StoredEmailEntity> RequiredAsync(
        IPersistenceSession session,
        StoredEmailId storedEmailId,
        CancellationToken cancellationToken)
    {
        var dbContext = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);

        return await dbContext.StoredEmails.FindAsync([storedEmailId.Value], cancellationToken)
            ?? throw new InvalidOperationException(
                "A conversation cannot be recorded against a stored email that no longer exists.");
    }

    private static IReadOnlyList<ThreadedEmail> Threaded(IEnumerable<StoredEmailEntity> emails) =>
    [
        .. emails
            .DistinctBy(email => email.Id)
            .OrderBy(email => email.Id)
            .Select(ThreadedEmails.Of),
    ];
}
