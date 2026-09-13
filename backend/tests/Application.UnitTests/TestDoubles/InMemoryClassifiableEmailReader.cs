// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Spam;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.TestSupport;

namespace MailFathom.Application.UnitTests.TestDoubles;

/// <summary>Answers every read of the port over a set of stored emails held in memory.</summary>
/// <remarks>
/// The walk is a keyset read ordered by the stored identity, which is what a resumed run depends on, so the double
/// implements exactly that rather than handing back whatever order it was arranged in. Each email belongs to one user,
/// and both single reads answer only for that user, as the real reader's predicates do.
/// </remarks>
internal sealed class InMemoryClassifiableEmailReader : IClassifiableEmailReader
{
    private readonly List<ClassifiableEmail> emails = [];
    private readonly Dictionary<StoredEmailId, MailUserId> ownersByEmail = [];
    private readonly Dictionary<EmailOccurrenceId, StoredEmailId> emailIdsByOccurrence = [];

    /// <summary>Gets the batch sizes the reads asked for, oldest first.</summary>
    internal List<int> RequestedBatchSizes { get; } = [];

    /// <summary>Stores one email the walk can reach, held by the deployment's synthetic user.</summary>
    /// <param name="email">The email to store.</param>
    /// <returns>Its identity, so a test can assert the order the walk reached it in.</returns>
    internal StoredEmailId Add(ClassifiableEmail email) => this.Add(email, SyntheticMailUser.Deployment);

    /// <summary>Stores one email the walk can reach, held by the user named.</summary>
    /// <param name="email">The email to store.</param>
    /// <param name="owner">The user whose mailbox holds it.</param>
    /// <returns>Its identity, so a test can assert the order the walk reached it in.</returns>
    internal StoredEmailId Add(ClassifiableEmail email, MailUserId owner)
    {
        this.emails.Add(email);
        this.ownersByEmail[email.Id] = owner;

        return email.Id;
    }

    /// <summary>Stores the occurrence one held email was discovered at, so a job payload naming it resolves.</summary>
    /// <param name="occurrenceId">The stable remote occurrence identity.</param>
    /// <param name="emailId">The local identity it was stored as.</param>
    internal void AddOccurrence(EmailOccurrenceId occurrenceId, StoredEmailId emailId) =>
        this.emailIdsByOccurrence[occurrenceId] = emailId;

    /// <inheritdoc />
    public Task<ClassifiableEmail?> FindAsync(
        MailUserId user,
        StoredEmailId emailId,
        CancellationToken cancellationToken) => Task.FromResult(
        this.emails.FirstOrDefault(email => email.Id == emailId && this.IsHeldBy(email.Id, user)));

    /// <inheritdoc />
    public Task<StoredEmailId?> FindStoredEmailIdAsync(
        MailUserId user,
        EmailOccurrenceId occurrenceId,
        CancellationToken cancellationToken) => Task.FromResult(
        this.emailIdsByOccurrence.TryGetValue(occurrenceId, out var emailId) && this.IsHeldBy(emailId, user)
            ? emailId
            : (StoredEmailId?)null);

    /// <inheritdoc />
    public Task<IReadOnlyList<ClassifiableEmail>> GetStoredEmailsAsync(
        MailAccountIdentity account,
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
                .Where(email => email.AccountId == account.Id)
                .Where(email => folderAliases.Contains(email.FolderAlias))
                .Where(email => resumeAfter is not { } position || email.Id.Value.CompareTo(position.Value) > 0)
                .OrderBy(email => email.Id.Value)
                .Take(batchSize),
        ];

        return Task.FromResult(batch);
    }

    private bool IsHeldBy(StoredEmailId emailId, MailUserId user) =>
        this.ownersByEmail.TryGetValue(emailId, out var owner) && owner == user;
}
