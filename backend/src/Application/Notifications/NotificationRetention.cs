// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;

namespace MailFathom.Application.Notifications;

/// <summary>Ages one owner's notifications out at the bound the record is held to.</summary>
/// <remarks>
/// <para>
/// A notification is derived personal data — it says what reached a person's mailbox and when — so a table nobody
/// prunes becomes a mailbox history in miniature, held for longer than anybody undertook to hold it. The bound is what
/// stops that, and it is the record's own rather than an account's: notifications belong to the owner, and an owner
/// reading one notification centre cannot have two answers about how long it remembers.
/// </para>
/// <para>
/// It rides the account's own synchronization run, beside the retention passes already there, for the reason those
/// ride it: an account already has a loop that comes round, and a schedule of its own would be another thing to
/// configure and watch for one bounded delete. An owner with several accounts is therefore swept once per account,
/// which costs a query that erases nothing rather than a second mechanism.
/// </para>
/// </remarks>
public sealed class NotificationRetention
{
    /// <summary>How long a notification is kept after the thing it describes happened.</summary>
    /// <remarks>
    /// It is a constant rather than a setting because there is no operator decision behind it to take. The audit
    /// trails are configurable because keeping a history of a person's mailbox is something a deployment undertakes
    /// deliberately and answers for; a notification is the client's own working state, produced whether anybody asked
    /// or not, and a knob for it would be configuration nobody has a reason to move. Three months is long enough that
    /// somebody returning from leave still finds what happened and short enough that the table stays a working set.
    /// </remarks>
    public static readonly TimeSpan Window = TimeSpan.FromDays(90);

    /// <summary>The greatest number of notifications one pass erases.</summary>
    /// <remarks>
    /// A pass that reaches the bound is followed by another on the account's next run, so the number decides how long
    /// a backlog takes to clear rather than what is kept. What it exists for is the backlog case — a deployment
    /// upgrading into this bound with months of notifications behind it — where an unbounded delete would lock the
    /// table against every raise behind it.
    /// </remarks>
    public const int MaximumNotificationsErasedPerPass = 5_000;

    private readonly INotificationStore store;
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes the retention pass from the record it erases from.</summary>
    /// <param name="store">Holds the notifications the pass erases from.</param>
    /// <param name="timeProvider">Measures the window back from now.</param>
    /// <exception cref="ArgumentNullException">Thrown when a required collaborator is <see langword="null" />.</exception>
    public NotificationRetention(INotificationStore store, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.store = store;
        this.timeProvider = timeProvider;
    }

    /// <summary>Erases everything of one owner's that has outlived the window.</summary>
    /// <param name="owner">The owner whose notifications are aged.</param>
    /// <param name="cancellationToken">Cancels the erasure.</param>
    /// <returns>How many notifications were erased.</returns>
    public Task<int> EraseExpiredAsync(MailOwnerId owner, CancellationToken cancellationToken) =>
        this.store.EraseOccurredBeforeAsync(
            owner,
            this.timeProvider.GetUtcNow() - Window,
            MaximumNotificationsErasedPerPass,
            cancellationToken);
}
