// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Storage;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.EmailContent.Repair;

/// <summary>Records the one defect a successful read discovers, for every use case that reads stored content.</summary>
/// <remarks>
/// Every other defect refuses the read, so the caller that found it is already writing a refusal and records the note
/// on its way out. This one does not: the bytes were served, from the copy the database retained beside an object that
/// could not be vouched for. Each reading use case would otherwise carry the same three lines, and a use case that
/// forgot them would read a deployment's mail through a broken object endpoint without anything saying so.
/// </remarks>
public static class EmailContentRepairRequestStoreExtensions
{
    /// <summary>Records that an object could not be vouched for, when the content served proves that it could not.</summary>
    /// <param name="repairRequestStore">The store the note is recorded in.</param>
    /// <param name="content">The content the read is about to answer with.</param>
    /// <param name="storedEmailId">The email that content belongs to.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>A task that completes after the note is durable, or immediately when there is nothing to note.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="repairRequestStore" /> or <paramref name="content" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// The read succeeds either way, because refusing over bytes the deployment is holding would be a self-inflicted
    /// outage. What the note buys is that the endpoint's failure is visible while releasing the retained copy is still
    /// a decision an operator has not yet taken; after the release the same situation is
    /// <see cref="EmailContentDefect.Missing" /> instead, discovered by a read that can no longer answer at all.
    /// </remarks>
    public static Task NoteIfServedFromRetainedCopyAsync(
        this IEmailContentRepairRequestStore repairRequestStore,
        StoredEmailContent content,
        StoredEmailId storedEmailId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(repairRequestStore);
        ArgumentNullException.ThrowIfNull(content);

        return content.WasServedFromRetainedCopy
            ? repairRequestStore.RecordAsync(
                new EmailContentRepairRequest(storedEmailId, EmailContentDefect.ObjectUnreadable),
                cancellationToken)
            : Task.CompletedTask;
    }
}
