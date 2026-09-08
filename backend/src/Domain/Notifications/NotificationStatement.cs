// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Notifications;

/// <summary>What a notification says, as the condition it was raised for and the numbers it is stated with.</summary>
/// <remarks>
/// <para>
/// It is what lets a row be drawn in the reader's own language: the service holds the condition and its counts, and
/// whoever renders the row supplies the sentence. The two numbers therefore mean whatever the cause says they mean,
/// which is why a statement is only ever built through the factory for one cause — a cause and a count that do not
/// belong together cannot be composed here at all.
/// </para>
/// <para>
/// It carries numbers and nothing else. A subject, an address, a filename, or any other fragment of mail would be a
/// second copy of the mailbox in a record that deliberately holds none, so a count is the widest thing a cause is ever
/// stated with.
/// </para>
/// </remarks>
public sealed record NotificationStatement
{
    private NotificationStatement(NotificationCause cause, int? counted, int? outOf)
    {
        this.Cause = cause;
        this.Counted = counted;
        this.OutOf = outOf;
    }

    /// <summary>Gets the condition the notification was raised for.</summary>
    public NotificationCause Cause { get; }

    /// <summary>Gets how many the cause counts, and <see langword="null" /> where it counts nothing.</summary>
    public int? Counted { get; }

    /// <summary>Gets how many the count above is out of, and <see langword="null" /> where the cause counts against nothing.</summary>
    public int? OutOf { get; }

    /// <summary>States that mail arrived, counting the messages one run committed.</summary>
    /// <param name="messageCount">How many messages arrived.</param>
    /// <returns>The statement.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="messageCount" /> is negative.</exception>
    public static NotificationStatement MailArrived(int messageCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(messageCount);

        return new NotificationStatement(NotificationCause.MailArrived, messageCount, outOf: null);
    }

    /// <summary>States that a run did not finish every folder it scheduled.</summary>
    /// <param name="failedFolderCount">How many of the run's folders did not finish.</param>
    /// <param name="scheduledFolderCount">How many folders the run scheduled.</param>
    /// <returns>The statement.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when either count is negative, or when more folders failed than were scheduled.</exception>
    public static NotificationStatement SynchronizationIncomplete(int failedFolderCount, int scheduledFolderCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(failedFolderCount);
        ArgumentOutOfRangeException.ThrowIfNegative(scheduledFolderCount);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(failedFolderCount, scheduledFolderCount);

        return new NotificationStatement(
            NotificationCause.SynchronizationIncomplete,
            failedFolderCount,
            scheduledFolderCount);
    }

    /// <summary>States that the mail server refused the credential an account holds.</summary>
    /// <returns>The statement.</returns>
    public static NotificationStatement CredentialRefused() =>
        new(NotificationCause.CredentialRefused, counted: null, outOf: null);

    /// <summary>Restores the statement a stored row holds, or nothing where the row names no cause.</summary>
    /// <param name="cause">The condition the row names, or <see langword="null" /> for a row written before a cause was kept.</param>
    /// <param name="counted">How many the cause counts.</param>
    /// <param name="outOf">How many the count is out of.</param>
    /// <returns>The statement, or <see langword="null" /> where the row names no cause.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="cause" /> is not a declared cause, or when a count is negative.</exception>
    /// <remarks>
    /// A row read back is input from outside this process however it got there, so it is validated exactly as a
    /// composed one is. What it does not do is hold a restored row to the shape its cause is composed with: a
    /// deployment upgraded over rows written by an older build has rows whose cause was inferred from nothing at all,
    /// and refusing to draw one would lose a notification rather than a number.
    /// </remarks>
    public static NotificationStatement? Restore(NotificationCause? cause, int? counted, int? outOf)
    {
        if (cause is not { } named)
        {
            return null;
        }

        if (!Enum.IsDefined(named))
        {
            throw new ArgumentOutOfRangeException(nameof(cause), named, "A notification names a declared cause.");
        }

        if (counted < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(counted), counted, "A notification counts nothing negative.");
        }

        if (outOf < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(outOf), outOf, "A notification counts against nothing negative.");
        }

        return new NotificationStatement(named, counted, outOf);
    }
}
