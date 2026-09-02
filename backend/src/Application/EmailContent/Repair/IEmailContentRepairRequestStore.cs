// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.EmailContent.Repair;

/// <summary>Records durably that an email's local content needs repairing, so a later run can act on it.</summary>
/// <remarks>
/// <para>
/// The port exists because a read discovers the defect and a synchronization run is what can fix it, and the two never
/// share a process lifetime. A counter in a log line or a queue held in memory would lose the finding the moment the
/// host restarted, which is the case a damaged local copy is most likely to be discovered in.
/// </para>
/// <para>
/// Performing the repair is deliberately not part of this contract: a read must never reach a mail server, so the most
/// this port may do is leave a durable note behind. It takes no persistence session for the same reason — the read it
/// belongs to opens no transaction, and the note is worth keeping whether or not the read that produced it completes.
/// </para>
/// </remarks>
public interface IEmailContentRepairRequestStore
{
    /// <summary>Records one email's repair request, replacing whatever was recorded for that email before.</summary>
    /// <param name="request">The email and the defect found in its stored content.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>A task that completes after durable storage.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="request" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// The operation is idempotent per email, because a caller reading the same damaged message repeatedly must leave
    /// one outstanding request rather than a row per attempt. An implementation records when the defect was first seen
    /// and when it was last seen, so a repair that never succeeds stays visible as one long-running problem.
    /// </remarks>
    Task RecordAsync(EmailContentRepairRequest request, CancellationToken cancellationToken);
}
