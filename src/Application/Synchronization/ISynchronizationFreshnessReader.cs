// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Application.Emails;

namespace MailMcp.Application.Synchronization;

/// <summary>Reads how current the local copy of each folder in a mailbox scope is.</summary>
/// <remarks>
/// The port exists separately from the readers that return mail because freshness is a property of synchronization
/// rather than of any one query: every read model attaches it, and each of them would otherwise re-derive it from the
/// same durable checkpoints. Implementations join no transaction and mutate nothing.
/// </remarks>
public interface ISynchronizationFreshnessReader
{
    /// <summary>Reads one freshness entry per folder the scope covers.</summary>
    /// <param name="scope">The accounts and folder aliases to report on, or <see cref="MailboxScope.Unrestricted" /> for every served account.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>One entry per known folder in the scope, ordered by account and then by alias.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="scope" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// A folder the scope names but no synchronization run has ever reached is reported with no timestamp rather than
    /// omitted, so a caller sees the folder it asked about. A folder that has never been discovered at all is unknown to
    /// local state and appears in no entry.
    /// </remarks>
    Task<IReadOnlyList<MailboxFolderFreshness>> ReadAsync(MailboxScope scope, CancellationToken cancellationToken);
}
