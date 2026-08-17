// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Emails.Threads;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;

namespace MailFathom.Application.UnitTests.TestDoubles;

/// <summary>Answers thread reads from a list held in memory, keyed by the thread each email belongs to.</summary>
/// <remarks>
/// <para>
/// A hand-written fake rather than a substitute, because what these tests arrange is a conversation rather than a call:
/// the ordering, the visibility narrowing, and the bound are all decided from what comes back, so the double's job is to
/// hold a set of emails and hand back the ones a thread names.
/// </para>
/// <para>
/// It keeps the guarantees the real reader's query makes rather than only its signature — the scope narrows the answer,
/// the identity orders it, and the bound cuts it one row past
/// <see cref="IEmailThreadReader.MaximumAssembledEmails" />. A fake that returned everything in whatever order a test
/// happened to write it would let a regression dropping any of the three pass every test that uses it.
/// </para>
/// </remarks>
internal sealed class StubEmailThreadReader(
    params IReadOnlyList<(EmailThreadId ThreadId, ThreadedEmailSummary Email)> emails)
    : IEmailThreadReader
{
    /// <summary>Gets how many times a thread was read, which is what proves one read assembles a conversation once.</summary>
    public int ReadCount { get; private set; }

    /// <summary>Reports whether a scope admits one email, which is what the real reader asks PostgreSQL.</summary>
    /// <param name="scope">The scope the read runs under.</param>
    /// <param name="email">The email to decide about.</param>
    /// <returns><see langword="true" /> when the account narrowing and the folder narrowing both admit it.</returns>
    internal static bool Admits(MailboxScope scope, ThreadedEmailSummary email)
    {
        var folder = new MailFolderIdentity(email.AccountId, email.FolderAlias);

        return (scope.AccountIds.Count is 0 || scope.AccountIds.Contains(email.AccountId))
            && scope.ReadableFolders.Contains(folder)
            && !scope.WithheldJunkFolders.Contains(folder);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ThreadedEmailSummary>> ReadEmailsAsync(
        EmailThreadId threadId,
        MailboxScope scope,
        CancellationToken cancellationToken)
    {
        this.ReadCount++;

        return Task.FromResult<IReadOnlyList<ThreadedEmailSummary>>(
        [
            .. emails
                .Where(held => held.ThreadId == threadId)
                .Select(held => held.Email)
                .Where(email => Admits(scope, email))
                .OrderBy(email => email.StoredEmailId.Value)
                .Take(IEmailThreadReader.MaximumAssembledEmails + 1),
        ]);
    }
}
