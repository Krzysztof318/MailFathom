// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Folders;

namespace MailFathom.Application.Synchronization.Sessions;

/// <summary>Waits on one long-lived session for the mail server to report that any of several folders changed.</summary>
/// <remarks>
/// <para>
/// This is the same contract as <see cref="IMailboxNotificationSession" /> over a set rather than one folder, and it
/// exists because a server that can report several folders over one connection makes the per-folder session a cost
/// rather than a design: an account with six folders otherwise holds six authenticated connections open for the
/// lifetime of the process.
/// </para>
/// <para>
/// It reads nothing, exactly as the single-folder session reads nothing. What it reports is which folder the server
/// named, and the synchronization that follows is the ordinary pass over its own read-only session. Nothing here may
/// fetch an envelope, a body, or a flag.
/// </para>
/// <para>
/// The session is long-lived, so its owner recycles it when the settings it was opened under are superseded. One
/// session is used by one waiter at a time and nothing here is safe for concurrent use.
/// </para>
/// </remarks>
public interface IMailboxFolderSetNotificationSession : IAsyncDisposable
{
    /// <summary>Waits until one of the watched folders changes or until the supplied wait elapses.</summary>
    /// <param name="maxWait">How long this call may wait before it returns having observed nothing.</param>
    /// <param name="cancellationToken">Ends the wait promptly and leaves the session usable.</param>
    /// <returns>The folder the server named, or the statement that the wait ended without one.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="maxWait" /> is negative or zero.</exception>
    /// <exception cref="MailboxUnavailableException">Thrown when the mail server did not hold the session up within its configured resilience budget.</exception>
    /// <exception cref="MailboxFolderRecreatedException">Thrown when a re-established connection reselected its folder with a different UIDVALIDITY.</exception>
    /// <remarks>
    /// <para>
    /// The bound belongs to the caller for the reason it does on the single-folder session: a push mechanism has to be
    /// re-issued before the server's own idle timeout, and an elapsed wait is how the session says it is ready to be
    /// renewed. An implementation treats that as an ordinary return and leaves the session usable.
    /// </para>
    /// <para>
    /// Cancellation ends the wait as <see cref="MailboxNotificationOutcome.WaitElapsed" /> rather than as an
    /// <see cref="OperationCanceledException" />, and the session stays open.
    /// </para>
    /// <para>
    /// Only the first folder to change is reported, because the pass that follows covers the whole account and there is
    /// nothing for a caller to do with the second name.
    /// </para>
    /// </remarks>
    Task<MailboxFolderSetNotificationOutcome> WaitForFolderChangeAsync(
        TimeSpan maxWait,
        CancellationToken cancellationToken);
}

/// <summary>States how a wait over a set of watched folders ended, and which folder ended it.</summary>
/// <param name="Outcome">Whether a folder changed or the wait simply elapsed.</param>
/// <param name="ChangedFolder">
/// The folder the server named, which is present exactly when <paramref name="Outcome" /> is
/// <see cref="MailboxNotificationOutcome.FolderChanged" />.
/// </param>
/// <remarks>
/// The alias is carried for the operator rather than for the pass: a run covers every folder of the account regardless,
/// and what an operator cannot otherwise see is which folder the server is actually reporting through the subscription.
/// </remarks>
public sealed record MailboxFolderSetNotificationOutcome(
    MailboxNotificationOutcome Outcome,
    MailFolderAlias? ChangedFolder)
{
    /// <summary>Gets the outcome of a wait that observed nothing and left the session ready to be renewed.</summary>
    public static MailboxFolderSetNotificationOutcome WaitElapsed { get; } =
        new(MailboxNotificationOutcome.WaitElapsed, ChangedFolder: null);

    /// <summary>Reports the folder the server said changed.</summary>
    /// <param name="folderAlias">The alias of the folder that changed.</param>
    /// <returns>An outcome naming the folder.</returns>
    public static MailboxFolderSetNotificationOutcome FolderChanged(MailFolderAlias folderAlias) =>
        new(MailboxNotificationOutcome.FolderChanged, folderAlias);
}
