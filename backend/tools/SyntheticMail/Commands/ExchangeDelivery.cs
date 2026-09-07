// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using MailFathom.SyntheticMail.Configuration;
using MailFathom.SyntheticMail.Delivery;
using MimeKit;

namespace MailFathom.SyntheticMail.Commands;

/// <summary>What both commands that deliver exchanges do around the delivery itself.</summary>
/// <remarks>
/// An exchange is delivered by one run that generated it and by another replaying a corpus it did not, and everything
/// either does around <see cref="SyntheticConversationDelivery" /> is the same: read the two accounts before anything
/// else happens, refuse a mailbox that is not the address being delivered to, open both sessions, and say where the
/// mail is going. Only where the turns come from differs, which is what <see cref="DeliverableTurn" /> carries.
/// </remarks>
internal static class ExchangeDelivery
{
    /// <summary>Reads the account an exchange submits as and the mailbox it is with, and refuses a pair that is not one exchange.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <param name="configurationPath">Where the two accounts are read from.</param>
    /// <param name="recipient">The mailbox the exchanges are delivered to.</param>
    /// <returns>The submission account and the mailbox MailFathom synchronizes.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <exception cref="SyntheticMailFailure">Thrown when either account is missing or incomplete, or the mailbox is not the address being delivered to.</exception>
    /// <remarks>
    /// Read before anything is generated or opened, so a run that could not have delivered says so at once rather than
    /// after a provider has written two hundred messages. The addresses have to agree because an exchange delivers to
    /// a mailbox, reads that mailbox back, and appends to it: two would fill one mailbox with half a thread and leave
    /// the other holding replies to messages it never received, which is invisible until somebody opens the client.
    /// </remarks>
    internal static (SendingAccount Account, WatchedMailboxAccount Mailbox) ReadAccounts(
        SyntheticMailContext context,
        string configurationPath,
        MailboxAddress recipient)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(configurationPath);
        ArgumentNullException.ThrowIfNull(recipient);

        var account = context.ReadAccount(configurationPath);
        var mailbox = context.ReadWatchedMailbox(configurationPath);

        return string.Equals(mailbox.Address.Address, recipient.Address, StringComparison.OrdinalIgnoreCase)
            ? (account, mailbox)
            : throw new SyntheticMailFailure(
                $"'{recipient.Address}' is not the mailbox configured as 'mailbox.address', which is '{mailbox.Address.Address}'. An exchange delivers to a mailbox, reads it back, and files in it, so those are one address.");
    }

    /// <summary>Opens both sessions and delivers every exchange through them.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <param name="account">The account the correspondent's half is submitted as.</param>
    /// <param name="mailbox">The mailbox MailFathom synchronizes, which the other half is appended to.</param>
    /// <param name="recipient">The address every exchange is with.</param>
    /// <param name="exchanges">The exchanges, each oldest turn first.</param>
    /// <param name="interval">How long to wait between two submissions.</param>
    /// <param name="deliveryTimeout">How long to wait for a submitted message to appear in the mailbox.</param>
    /// <param name="cancellationToken">Cancels the run.</param>
    /// <returns>What was delivered and what was not.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    internal static async Task<DeliveryReport> DeliverAsync(
        SyntheticMailContext context,
        SendingAccount account,
        WatchedMailboxAccount mailbox,
        MailboxAddress recipient,
        IReadOnlyList<IReadOnlyList<DeliverableTurn>> exchanges,
        TimeSpan interval,
        TimeSpan deliveryTimeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(mailbox);
        ArgumentNullException.ThrowIfNull(recipient);
        ArgumentNullException.ThrowIfNull(exchanges);

        context.Console.WriteError(string.Create(
            CultureInfo.InvariantCulture,
            $"Submitting as {account.Address.Address} to {account.Host}:{account.Port} over {account.Security}, and reading {mailbox.Address.Address} at {mailbox.Host}:{mailbox.Port} over {mailbox.Security}."));

        await using var transport = context.OpenTransport(account);
        await using var watched = context.OpenWatchedMailbox(mailbox);

        await transport.OpenAsync(cancellationToken);
        await watched.OpenAsync(cancellationToken);

        return await new SyntheticConversationDelivery(transport, watched, context.Console, context.Clock).DeliverAsync(
            exchanges,
            account,
            recipient,
            interval,
            deliveryTimeout,
            cancellationToken);
    }

    /// <summary>Writes what became of a delivery and reports the exit code it earns.</summary>
    /// <param name="console">Where the run reports what it did.</param>
    /// <param name="recipient">The mailbox the run was delivering to.</param>
    /// <param name="report">What was delivered and what was not.</param>
    /// <returns>The exit code.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <remarks>
    /// A batch in which any message failed reports the failure code even though the rest were delivered, because a
    /// script that fills a mailbox and then asserts against it has to know the mailbox is not the one the corpus
    /// describes.
    /// </remarks>
    internal static int Report(ISyntheticMailConsole console, MailboxAddress recipient, DeliveryReport report)
    {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(recipient);
        ArgumentNullException.ThrowIfNull(report);

        console.WriteError(string.Create(
            CultureInfo.InvariantCulture,
            $"Delivered {report.Delivered} of {report.Attempted} to {recipient.Address}."));

        if (report.Failures.Count == 0)
        {
            return SyntheticMailExitCode.Success;
        }

        foreach (var failure in report.Failures)
        {
            console.WriteError($"  refused <{failure.MessageId}> \"{failure.Subject}\": {failure.Reason}");
        }

        return SyntheticMailExitCode.Failure;
    }
}
