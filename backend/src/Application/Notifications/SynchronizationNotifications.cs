// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using MailFathom.Application.Accounts;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Notifications;

namespace MailFathom.Application.Notifications;

/// <summary>Turns what a synchronization run observed into what the account's assigned users are told about it.</summary>
/// <remarks>
/// <para>
/// The run is the only producer wired today, and it produces two things: that mail arrived, and that something about
/// the account needs a person. Both are facts the service holds and the client cannot derive, which is the whole
/// reason the record exists.
/// </para>
/// <para>
/// Mail is reported once per run rather than once per message. A run that commits forty messages is one arrival to the
/// person who was away from the screen, and forty rows would bury every other kind of notification under one
/// mailbox's traffic. The count is the run's, and the row leads to the mailbox rather than to any one message,
/// because there is no one message a run is about.
/// </para>
/// <para>
/// Nothing composed here reads mail. A count, an account identifier, and a fixed sentence are what a row carries, so
/// no subject, address, body fragment, filename, or credential can reach the record, a log, or a telemetry event
/// through this path.
/// </para>
/// <para>
/// Every raise states its condition twice, in the two forms a notification is read in. The statement is the condition
/// itself and the numbers it is made with, which is what a client turns into a sentence in whatever language the
/// person reading it has; the title and the body beside it are that same condition written out in English, for a
/// reader with no client to say it in their own. Both are composed here from the same values, so the two cannot come
/// to describe different runs.
/// </para>
/// <para>
/// A row that was kept is also announced to whatever the user has open, which is <see cref="NotificationRaiser" />'s
/// half of the raise rather than this producer's: writing and announcing are one act, and a row the deduplication rule
/// folded into a standing one is not announced for the same reason it is not written.
/// </para>
/// <para>
/// A notification is one person's, so a mailbox assigned to several people is one condition raised once for each of
/// them: the run observed one thing about one account, and each of the people served by it has their own unread list
/// and their own deduplication. An account assigned to nobody raises nothing, which is the same answer the signal
/// channel gives — there is nobody the condition is about.
/// </para>
/// </remarks>
public sealed class SynchronizationNotifications
{
    private readonly NotificationRaiser raiser;
    private readonly IMailAccountAssignments assignments;
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes the producer from the record it raises into.</summary>
    /// <param name="raiser">Keeps what is raised and announces it to whatever the person has open.</param>
    /// <param name="assignments">Answers who a condition about one mailbox is about.</param>
    /// <param name="timeProvider">Stamps a notification with when the run observed what it describes.</param>
    /// <exception cref="ArgumentNullException">Thrown when a required collaborator is <see langword="null" />.</exception>
    public SynchronizationNotifications(
        NotificationRaiser raiser,
        IMailAccountAssignments assignments,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(raiser);
        ArgumentNullException.ThrowIfNull(assignments);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.raiser = raiser;
        this.assignments = assignments;
        this.timeProvider = timeProvider;
    }

    /// <summary>Reports the mail one run committed, as one notification for the run.</summary>
    /// <param name="account">The account the run was over.</param>
    /// <param name="newMessageCount">How many messages the run committed locally.</param>
    /// <param name="cancellationToken">Cancels the raise.</param>
    /// <returns><see langword="true" /> when a notification was kept.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="newMessageCount" /> is negative.</exception>
    /// <remarks>
    /// A run that committed nothing says nothing: an empty run is the ordinary case and is not an event anybody was
    /// away from the screen for. Arrival is deduplicated like every other condition, so a standing unread arrival
    /// keeps the count of the run that raised it and a later run's mail adds no second row: what the row says is that
    /// mail arrived rather than how much is waiting, and the mailbox answers the second question when it is opened.
    /// </remarks>
    public Task<bool> ReportArrivedMailAsync(
        MailAccountId account,
        int newMessageCount,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(newMessageCount);

        if (newMessageCount == 0)
        {
            return Task.FromResult(false);
        }

        return this.RecordAsync(
            account,
            NotificationKind.Mail,
            title: "New mail",
            body: newMessageCount == 1
                ? "1 new message arrived."
                : string.Create(CultureInfo.InvariantCulture, $"{newMessageCount} new messages arrived."),
            NotificationStatement.MailArrived(newMessageCount),
            NotificationTarget.ToScreen(NotificationScreen.Mail),
            condition: "mail-arrived",
            cancellationToken);
    }

    /// <summary>Reports a run that ended with folders it did not finish.</summary>
    /// <param name="account">The account the run was over.</param>
    /// <param name="failedFolderCount">How many of the run's folders did not finish.</param>
    /// <param name="scheduledFolderCount">How many folders the run scheduled.</param>
    /// <param name="cancellationToken">Cancels the raise.</param>
    /// <returns><see langword="true" /> when a notification was kept.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when either count is negative.</exception>
    /// <remarks>
    /// A run that finished everything it scheduled says nothing, because a working mailbox is not news. The row leads
    /// nowhere: there is nothing for the person to open, and the account will try again on its own.
    /// </remarks>
    public Task<bool> ReportIncompleteRunAsync(
        MailAccountId account,
        int failedFolderCount,
        int scheduledFolderCount,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(failedFolderCount);
        ArgumentOutOfRangeException.ThrowIfNegative(scheduledFolderCount);

        if (failedFolderCount == 0)
        {
            return Task.FromResult(false);
        }

        return this.RecordAsync(
            account,
            NotificationKind.System,
            title: "Some mail could not be fetched",
            body: string.Create(
                CultureInfo.InvariantCulture,
                $"{failedFolderCount} of {scheduledFolderCount} folders did not finish. MailFathom will try again."),
            NotificationStatement.SynchronizationIncomplete(failedFolderCount, scheduledFolderCount),
            NotificationTarget.Nothing,
            condition: "synchronization-incomplete",
            cancellationToken);
    }

    /// <summary>Reports an account whose credential the mail server refused.</summary>
    /// <param name="account">The account the server refused.</param>
    /// <param name="cancellationToken">Cancels the raise.</param>
    /// <returns><see langword="true" /> when a notification was kept.</returns>
    /// <remarks>
    /// This is the condition the deduplication rule exists for. A refused credential is refused again on every run
    /// until somebody replaces it, so the statement is made once and stays made until it has been read.
    /// </remarks>
    public Task<bool> ReportRefusedCredentialAsync(
        MailAccountId account,
        CancellationToken cancellationToken) =>
        this.RecordAsync(
            account,
            NotificationKind.System,
            title: "This account needs signing in again",
            body: "The mail server refused the credential MailFathom holds, so this account is no longer being fetched.",
            NotificationStatement.CredentialRefused(),
            NotificationTarget.ToScreen(NotificationScreen.Settings),
            condition: "credential-refused",
            cancellationToken);

    private async Task<bool> RecordAsync(
        MailAccountId account,
        NotificationKind kind,
        string title,
        string body,
        NotificationStatement statement,
        NotificationTarget target,
        string condition,
        CancellationToken cancellationToken)
    {
        var occurredAt = this.timeProvider.GetUtcNow();
        var accountId = account.Value;

        // An account identifier is the operator's own text and nothing bounds its length, while both places one
        // reaches here are bounded columns. The key reduces an outsized one to a digest of itself; the source line is
        // a label rather than an identity, so an identifier that cannot be shown leaves the kind as the whole of it
        // rather than being shown truncated as an account nobody configured.
        var source = accountId.Length <= Notification.MaximumSourceLength ? accountId : null;

        var anyKept = false;

        // One row per assigned user rather than one for the mailbox, because the unread list, the read mark, and the
        // deduplication window are each one person's. The identity is generated per row for the same reason: two
        // people told the same thing are two rows either of them may read or dismiss without touching the other's.
        foreach (var user in this.assignments.UsersAssignedTo(account))
        {
            var notification = Notification.Compose(
                NotificationId.Create(Guid.CreateVersion7(occurredAt)),
                user,
                kind,
                title,
                body,
                statement,
                source,
                target,
                NotificationDeduplicationKey.For(condition, account.Value),
                occurredAt);

            anyKept |= await this.raiser.RecordAndAnnounceAsync(notification, cancellationToken);
        }

        return anyKept;
    }
}
