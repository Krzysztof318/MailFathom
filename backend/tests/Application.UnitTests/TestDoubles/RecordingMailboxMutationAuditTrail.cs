// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Mail.Mutations.Audit;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;
using MailFathom.Domain.Mutations.Audit;

namespace MailFathom.Application.UnitTests.TestDoubles;

/// <summary>Keeps the audit entries a run appended, and reproduces the one rule the trail decides for itself.</summary>
/// <remarks>
/// That rule is whether an entry is owed at all, which the record carries and no caller re-reads. Everything else the
/// real trail does — committing in a transaction of its own and swallowing a failure — belongs to the adapter and is
/// proven against a real database, so the double appends in memory and, when a test asks it to, refuses in the same way
/// the real one absorbs a refusal.
/// </remarks>
internal sealed class RecordingMailboxMutationAuditTrail : IMailboxMutationAuditTrail
{
    private readonly List<MailboxMutationAuditEntry> entries = [];
    private DateTimeOffset completedAt = new(2026, 8, 7, 13, 0, 0, TimeSpan.Zero);

    /// <summary>Gets the entries appended, in the order the mutations ended.</summary>
    internal IReadOnlyList<MailboxMutationAuditEntry> Entries => this.entries;

    /// <summary>Gets or sets whether every append fails, as a trail whose database is unreachable would.</summary>
    internal bool FailsEveryAppend { get; set; }

    /// <inheritdoc />
    public Task RecordAsync(
        MailboxMutationRecord record,
        MailFolderResolution sourceFolder,
        CancellationToken cancellationToken)
    {
        if (!record.IsAudited)
        {
            return Task.CompletedTask;
        }

        if (this.FailsEveryAppend)
        {
            // Swallowed exactly as the adapter swallows it: the change has already been made and the trail may not
            // fail the operation that made it.
            return Task.CompletedTask;
        }

        this.completedAt = this.completedAt.AddSeconds(1);
        this.entries.Add(MailboxMutationAuditEntry.Of(
            MailboxMutationAuditEntryId.Create(Guid.CreateVersion7(this.completedAt)),
            record,
            sourceFolder,
            this.completedAt));

        return Task.CompletedTask;
    }
}
