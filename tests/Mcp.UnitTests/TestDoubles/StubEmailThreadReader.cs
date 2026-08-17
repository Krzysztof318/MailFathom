// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Emails.Threads;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;

namespace MailFathom.Mcp.UnitTests.TestDoubles;

/// <summary>Answers thread reads from a list held in memory, keyed by the thread each email belongs to.</summary>
/// <remarks>
/// It keeps the guarantees the real reader's query makes rather than only its signature: the scope narrows the answer,
/// the identity orders it, and the bound cuts it one row past
/// <see cref="IEmailThreadReader.MaximumAssembledEmails" />.
/// </remarks>
internal sealed class StubEmailThreadReader(
    params IReadOnlyList<(EmailThreadId ThreadId, ThreadedEmailSummary Email)> emails)
    : IEmailThreadReader
{
    /// <inheritdoc />
    public Task<IReadOnlyList<ThreadedEmailSummary>> ReadEmailsAsync(
        EmailThreadId threadId,
        MailboxScope scope,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ThreadedEmailSummary>>(
        [
            .. emails
                .Where(held => held.ThreadId == threadId)
                .Select(held => held.Email)
                .Where(email => Admits(scope, email))
                .OrderBy(email => email.StoredEmailId.Value)
                .Take(IEmailThreadReader.MaximumAssembledEmails + 1),
        ]);

    private static bool Admits(MailboxScope scope, ThreadedEmailSummary email)
    {
        var folder = new MailFolderIdentity(email.AccountId, email.FolderAlias);

        return (scope.AccountIds.Count is 0 || scope.AccountIds.Contains(email.AccountId))
            && scope.ReadableFolders.Contains(folder)
            && !scope.WithheldJunkFolders.Contains(folder);
    }
}
