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
/// It keeps the guarantees the real reader's query makes rather than only its signature: an identifier published before
/// a merge resolves to the conversation that survived it, the scope narrows the answer, the identity orders it, and the
/// bound cuts it one row past <see cref="IEmailThreadReader.MaximumAssembledEmails" />.
/// </remarks>
internal sealed class StubEmailThreadReader(
    params IReadOnlyList<(EmailThreadId ThreadId, ThreadedEmailSummary Email)> emails)
    : IEmailThreadReader
{
    private readonly Dictionary<EmailThreadId, EmailThreadId> survivors = [];

    /// <summary>Records one conversation as folded into another, the way a merge leaves the table.</summary>
    /// <param name="merged">The conversation that was folded away, whose identifier a tool may already have published.</param>
    /// <param name="survivor">The conversation it was folded into.</param>
    /// <returns>The same reader, so a merge can be recorded where the reader is constructed.</returns>
    internal StubEmailThreadReader MergedInto(EmailThreadId merged, EmailThreadId survivor)
    {
        this.survivors[merged] = survivor;

        return this;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ThreadedEmailSummary>> ReadEmailsAsync(
        EmailThreadId threadId,
        MailboxScope scope,
        CancellationToken cancellationToken)
    {
        var surviving = this.Surviving(threadId);

        return Task.FromResult<IReadOnlyList<ThreadedEmailSummary>>(
        [
            .. emails
                .Where(held => held.ThreadId == surviving)
                .Select(held => held.Email)
                .Where(email => Admits(scope, email))
                .OrderBy(email => email.StoredEmailId.Value)
                .Take(IEmailThreadReader.MaximumAssembledEmails + 1),
        ]);
    }

    /// <summary>Follows the merges recorded here to the conversation that survived them.</summary>
    /// <remarks>
    /// The walk takes at most as many steps as merges were recorded, so a test recording a chain that loops fails on
    /// what it asserts rather than never returning.
    /// </remarks>
    private EmailThreadId Surviving(EmailThreadId threadId)
    {
        var surviving = threadId;

        for (var step = 0; step < this.survivors.Count; step++)
        {
            if (!this.survivors.TryGetValue(surviving, out var folded))
            {
                break;
            }

            surviving = folded;
        }

        return surviving;
    }

    private static bool Admits(MailboxScope scope, ThreadedEmailSummary email)
    {
        var folder = new MailFolderIdentity(email.AccountId, email.FolderAlias);

        return (scope.AccountIds.Count is 0 || scope.AccountIds.Contains(email.AccountId))
            && scope.ReadableFolders.Contains(folder)
            && !scope.WithheldJunkFolders.Contains(folder);
    }
}
