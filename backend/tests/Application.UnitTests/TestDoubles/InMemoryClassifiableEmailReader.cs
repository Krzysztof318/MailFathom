// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Spam;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;

namespace MailFathom.Application.UnitTests.TestDoubles;

/// <summary>Answers both reads of the port over a set of stored occurrences held in memory.</summary>
/// <remarks>
/// The walk is a keyset read ordered by the occurrence's identity, which is what a resumed run depends on, so the double
/// implements exactly that rather than handing back whatever order it was arranged in.
/// </remarks>
internal sealed class InMemoryClassifiableEmailReader : IClassifiableEmailReader
{
    private readonly List<ClassifiableEmail> emails = [];
    private readonly Dictionary<EmailOccurrenceId, StoredEmailId> emailIdsByOccurrence = [];

    /// <summary>Gets the batch sizes the reads asked for, oldest first.</summary>
    internal List<int> RequestedBatchSizes { get; } = [];

    /// <summary>Stores one occurrence the walk can reach.</summary>
    /// <param name="email">The occurrence to store.</param>
    /// <returns>Its identity, so a test can assert the order the walk reached it in.</returns>
    internal StoredEmailId Add(ClassifiableEmail email)
    {
        this.emails.Add(email);

        return email.Id;
    }

    /// <summary>Stores the occurrence one held email was discovered at, so a job payload naming it resolves.</summary>
    /// <param name="occurrenceId">The stable remote occurrence identity.</param>
    /// <param name="emailId">The local identity it was stored as.</param>
    internal void AddOccurrence(EmailOccurrenceId occurrenceId, StoredEmailId emailId) =>
        this.emailIdsByOccurrence[occurrenceId] = emailId;

    /// <inheritdoc />
    public Task<ClassifiableEmail?> FindAsync(StoredEmailId emailId, CancellationToken cancellationToken) =>
        Task.FromResult(this.emails.FirstOrDefault(email => email.Id == emailId));

    /// <inheritdoc />
    public Task<StoredEmailId?> FindStoredEmailIdAsync(
        EmailOccurrenceId occurrenceId,
        CancellationToken cancellationToken) => Task.FromResult(
        this.emailIdsByOccurrence.TryGetValue(occurrenceId, out var emailId) ? emailId : (StoredEmailId?)null);

    /// <inheritdoc />
    public Task<IReadOnlyList<ClassifiableEmail>> GetStoredEmailsAsync(
        MailAccountId accountId,
        IReadOnlyList<MailFolderAlias> folderAliases,
        StoredEmailId? resumeAfter,
        int batchSize,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(folderAliases);

        this.RequestedBatchSizes.Add(batchSize);

        IReadOnlyList<ClassifiableEmail> batch =
        [
            .. this.emails
                .Where(email => email.AccountId == accountId)
                .Where(email => folderAliases.Contains(email.FolderAlias))
                .Where(email => resumeAfter is not { } position || email.Id.Value.CompareTo(position.Value) > 0)
                .OrderBy(email => email.Id.Value)
                .Take(batchSize),
        ];

        return Task.FromResult(batch);
    }
}
