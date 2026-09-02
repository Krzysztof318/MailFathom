// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Synchronization.Sessions;

/// <summary>Waits on a long-lived session for the mail server to report that one folder changed.</summary>
/// <remarks>
/// <para>
/// This session reads nothing. It reports that a folder changed and never what changed, so the synchronization pass a
/// notification starts is the ordinary one — the same code path polling runs, over its own read-only session, with the
/// same bounds and the same <c>\Seen</c> guarantee. Nothing here may fetch an envelope, a body, or a flag: a second
/// retrieval path is exactly what would let the read-only invariant hold in one place and lapse in the other.
/// </para>
/// <para>
/// The session is long-lived by nature, which is what makes it the one place a rotated credential could otherwise stay
/// in use. Its owner recycles it when the settings it was opened under are superseded, so the connection is the
/// operation boundary here that a per-run connect is everywhere else.
/// </para>
/// <para>
/// One session is used by one waiter at a time, and nothing here is safe for concurrent use.
/// </para>
/// </remarks>
public interface IMailboxNotificationSession : IAsyncDisposable
{
    /// <summary>Waits until the folder changes or until the supplied wait elapses, whichever comes first.</summary>
    /// <param name="maxWait">How long this call may wait before it returns having observed nothing.</param>
    /// <param name="cancellationToken">Ends the wait promptly and leaves the session usable, which is how a caller with several sessions stops the rest once one of them reported a change.</param>
    /// <returns>Whether the folder changed or the wait ended without one.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="maxWait" /> is negative or zero.</exception>
    /// <exception cref="MailboxUnavailableException">Thrown when the mail server did not hold the session up within its configured resilience budget.</exception>
    /// <exception cref="MailboxFolderRecreatedException">Thrown when a re-established connection reselected the folder with a different UIDVALIDITY.</exception>
    /// <remarks>
    /// <para>
    /// The bound belongs to the caller because renewal is a scheduling decision rather than a protocol one: a push
    /// mechanism has to be re-issued before the server's own idle timeout, and returning
    /// <see cref="MailboxNotificationOutcome.WaitElapsed" /> is how the session says it is ready to be renewed. An
    /// implementation must therefore treat an elapsed wait as an ordinary return and leave the session usable.
    /// </para>
    /// <para>
    /// A wait is never repeated on the caller's behalf. Retrying one would re-enter a wait whose result the caller has
    /// already been told nothing about, and the retry budgets that cover an ordinary read are measured in seconds while
    /// a wait here is measured in minutes.
    /// </para>
    /// <para>
    /// Cancellation ends the wait as <see cref="MailboxNotificationOutcome.WaitElapsed" /> rather than as an
    /// <see cref="OperationCanceledException" />, and the session stays open. A caller cancels this to stop waiting,
    /// not to abandon the session, and the two are the same thing only at shutdown — where the caller disposes the
    /// session next anyway. Reporting a cancelled wait as a failure instead would make one folder's notification cost
    /// every other watched folder its connection.
    /// </para>
    /// </remarks>
    Task<MailboxNotificationOutcome> WaitForFolderChangeAsync(TimeSpan maxWait, CancellationToken cancellationToken);
}

/// <summary>States why a wait for a folder change ended.</summary>
public enum MailboxNotificationOutcome
{
    /// <summary>The mail server reported a change, so a synchronization pass should run now.</summary>
    FolderChanged = 0,

    /// <summary>Nothing was reported within the supplied wait, so the session is ready to be renewed.</summary>
    WaitElapsed = 1,
}
