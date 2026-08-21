// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;

namespace MailFathom.Application.Mail.Mutations.Audit;

/// <summary>Writes the durable history one finished mutation leaves behind.</summary>
/// <remarks>
/// <para>
/// It is asked of every mutation that ends, and it decides for itself whether an entry is written: the record carries
/// the answer that was resolved when it was opened, so the caller never has to read a setting the mutation already
/// settled.
/// </para>
/// <para>
/// <strong>It fails no mutation for a reason of its own, and rolls nothing back.</strong> The change has already been
/// made to somebody's mailbox by the time this is called, and a history that could undo it — or fail the operation that
/// produced it — would be worse than a history with a hole in it. Every entry that does not get written is reported and
/// counted where an operator can see it, and the mutation stands. The one thing that travels on is the caller's own
/// cancellation, which is reported the same way before it is re-raised.
/// </para>
/// </remarks>
public interface IMailboxMutationAuditTrail
{
    /// <summary>Appends the entry one terminal mutation record states, where the account's trail is on.</summary>
    /// <param name="record">The mutation record, at the terminal stage it ended in.</param>
    /// <param name="sourceFolder">The binding the source occurrence was read under, which supplies its remote path.</param>
    /// <param name="cancellationToken">Cancels the durable write.</param>
    /// <returns>A task that completes once the entry is durable, was refused, or was not owed at all.</returns>
    Task RecordAsync(
        MailboxMutationRecord record,
        MailFolderResolution sourceFolder,
        CancellationToken cancellationToken);
}
