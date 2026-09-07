// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using MailFathom.SyntheticMail.Configuration;
using MailFathom.SyntheticMail.Generation;
using MimeKit;

namespace MailFathom.SyntheticMail.Delivery;

/// <summary>Delivers exchanges one turn at a time, so each reply answers the message that actually arrived.</summary>
/// <param name="transport">The open submission session the correspondent's half is sent through.</param>
/// <param name="mailbox">The open session against the mailbox MailFathom synchronizes.</param>
/// <param name="console">Where the run reports what it is doing, which is standard error and never the corpus.</param>
/// <param name="timeProvider">What the pacing and the delivery wait run on.</param>
/// <remarks>
/// <para>
/// This is the part a flat batch cannot do. A batch composes every message from identifiers it invented and submits
/// them in any order, so its threading holds only while the submission server leaves <c>Message-Id</c> alone. An
/// exchange submits one turn, waits for the copy to appear in the mailbox, reads the identifier the server assigned
/// to it, and builds the next turn's ancestry from that — which is why the ancestry a turn was composed with is
/// replaced here rather than delivered as it stands.
/// </para>
/// <para>
/// The two sides reach the mailbox by different routes, and both routes end in it. The correspondent's half is
/// submitted by the sending account and delivered; the mailbox's own half is appended to its Sent folder, which is
/// where a mail client puts what it wrote. Both halves therefore synchronize, and a thread reads as message, reply,
/// message, reply rather than as a run of inbound mail.
/// </para>
/// <para>
/// What produced a turn is not one of its inputs: a run that generated the corpus and one replaying an exported one
/// hand over the same <see cref="DeliverableTurn" /> and get the same delivery. That is deliberate — the identifier
/// rewriting below is the difficult part of this tool, and a second copy of it written for replayed mail would be the
/// one that drifts.
/// </para>
/// </remarks>
internal sealed class SyntheticConversationDelivery(
    ISyntheticMailTransport transport,
    IWatchedMailbox mailbox,
    ISyntheticMailConsole console,
    TimeProvider timeProvider)
{
    /// <summary>How long the run waits between two looks for a delivered copy.</summary>
    /// <remarks>
    /// Long enough that a wait of a minute costs a server twenty commands rather than six hundred, and short enough
    /// that an exchange of six turns against a fast server is not spent waiting. It is not the pacing interval: that
    /// one exists so a burst of submissions is not refused, and this one is a poll.
    /// </remarks>
    private static readonly TimeSpan DeliveryPollInterval = TimeSpan.FromSeconds(3);

    /// <summary>Delivers every exchange, continuing with the next one when a turn fails.</summary>
    /// <param name="exchanges">The exchanges, each oldest turn first and each alternating from the correspondent onwards.</param>
    /// <param name="account">The account being submitted as, which decides the author of the correspondent's half.</param>
    /// <param name="watchedMailbox">The mailbox MailFathom synchronizes, which every exchange is with.</param>
    /// <param name="interval">How long to wait between two submissions.</param>
    /// <param name="deliveryTimeout">How long to wait for a submitted message to appear in the mailbox.</param>
    /// <param name="cancellationToken">Cancels the run, which stops it rather than recording a failure.</param>
    /// <returns>What was delivered and what was not.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    internal async Task<DeliveryReport> DeliverAsync(
        IReadOnlyList<IReadOnlyList<DeliverableTurn>> exchanges,
        SendingAccount account,
        MailboxAddress watchedMailbox,
        TimeSpan interval,
        TimeSpan deliveryTimeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(exchanges);
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(watchedMailbox);

        var failures = new List<DeliveryFailure>();
        var attempted = 0;
        var delivered = 0;

        for (var index = 0; index < exchanges.Count; index++)
        {
            var turns = exchanges[index];

            var deliveredTurns = await this.DeliverExchangeAsync(
                turns,
                account,
                watchedMailbox,
                interval,
                deliveryTimeout,
                attempted > 0,
                failures,
                string.Create(CultureInfo.InvariantCulture, $"Exchange {index + 1} of {exchanges.Count}"),
                cancellationToken);

            attempted += turns.Count;
            delivered += deliveredTurns;
        }

        return new DeliveryReport(attempted, delivered, failures);
    }

    /// <summary>Delivers one exchange, and abandons the rest of it as soon as a turn fails.</summary>
    /// <remarks>
    /// Abandoned rather than continued, because every remaining turn answers the one that failed: a reply built on an
    /// identifier that was never assigned is exactly the broken threading an exchange exists to replace. The
    /// abandoned turns are still reported, so the count a run prints stays the count the corpus described.
    /// </remarks>
    private async Task<int> DeliverExchangeAsync(
        IReadOnlyList<DeliverableTurn> turns,
        SendingAccount account,
        MailboxAddress watchedMailbox,
        TimeSpan interval,
        TimeSpan deliveryTimeout,
        bool paceFirstTurn,
        List<DeliveryFailure> failures,
        string exchange,
        CancellationToken cancellationToken)
    {
        // An exchange opens with the correspondent, so the first turn's author is the person on the other side of it.
        // Whichever address the correspondent's half was authored from is the one the mailbox writes back to, so a
        // reply is addressed to the person a reader would have replied to rather than to a participant the inbound
        // message only mentioned.
        var repliedTo = account.AuthorIdentity == SyntheticAuthorIdentity.Fabricated
            ? turns[0].Author
            : account.Address;

        // Every identifier the mailbox has actually assigned in this exchange, oldest first. It is both halves of the
        // ancestry a reply carries: the whole list is its `References` and the last entry is its `In-Reply-To`.
        var ancestry = new List<string>();
        var delivered = 0;

        for (var turn = 0; turn < turns.Count; turn++)
        {
            if ((turn > 0 || paceFirstTurn) && interval > TimeSpan.Zero)
            {
                await Task.Delay(interval, timeProvider, cancellationToken);
            }

            var assignedMessageId = await this.DeliverTurnAsync(
                turns[turn],
                SyntheticConversation.SideOf(turn),
                ancestry,
                account,
                watchedMailbox,
                repliedTo,
                deliveryTimeout,
                failures,
                string.Create(CultureInfo.InvariantCulture, $"{exchange}, turn {turn + 1} of {turns.Count}"),
                cancellationToken);

            if (assignedMessageId is null)
            {
                AbandonRemainingTurns(turns, turn, failures);

                return delivered;
            }

            ancestry.Add(assignedMessageId);
            delivered++;
        }

        return delivered;
    }

    /// <summary>Delivers one turn and reports the identifier the mailbox now holds it under, or nothing when it failed.</summary>
    private async Task<string?> DeliverTurnAsync(
        DeliverableTurn turn,
        SyntheticThreadSide side,
        IReadOnlyList<string> ancestry,
        SendingAccount account,
        MailboxAddress watchedMailbox,
        MailboxAddress repliedTo,
        TimeSpan deliveryTimeout,
        List<DeliveryFailure> failures,
        string position,
        CancellationToken cancellationToken)
    {
        try
        {
            using var message = turn.Compose();

            SyntheticMimeComposer.Thread(message, ancestry);

            if (side == SyntheticThreadSide.Mailbox)
            {
                console.WriteError($"{position}: appending to Sent.");

                SyntheticMimeComposer.AddressAsCorrespondence(message, watchedMailbox, repliedTo);

                await mailbox.AppendToSentAsync(message, cancellationToken);

                console.WriteError($"{position}: appended.");

                // The appended copy is the one this run composed, so nothing rewrote its identifier on the way in and
                // there is nothing to read back.
                return turn.MessageId;
            }

            console.WriteError($"{position}: submitting to {watchedMailbox.Address}.");

            SyntheticMimeComposer.AddressAsSubmission(
                message,
                turn.Author,
                watchedMailbox,
                account.Address,
                account.AuthorIdentity);

            SyntheticDeliveryMarker.Stamp(message, turn.MessageId);

            await transport.SendAsync(message, watchedMailbox, cancellationToken);

            console.WriteError(string.Create(
                CultureInfo.InvariantCulture,
                $"{position}: waiting up to {deliveryTimeout.TotalSeconds:0} seconds for it to reach the mailbox."));

            var assigned = await this.AwaitDeliveredMessageIdAsync(turn.MessageId, deliveryTimeout, cancellationToken);

            console.WriteError(assigned is null ? $"{position}: never arrived." : $"{position}: delivered.");

            if (assigned is null)
            {
                failures.Add(new DeliveryFailure(
                    turn.MessageId,
                    turn.Subject,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"submitted, but no copy of it reached the mailbox within {deliveryTimeout.TotalSeconds:0} seconds")));
            }

            return assigned;
        }
        catch (SyntheticMailFailure failure)
        {
            console.WriteError($"{position}: failed.");
            failures.Add(new DeliveryFailure(turn.MessageId, turn.Subject, failure.Message));

            return null;
        }
    }

    /// <summary>Waits, within a bound, for a submitted message to appear in the mailbox.</summary>
    /// <remarks>
    /// The probe comes before the bound is checked, so a timeout of nothing still looks once: a run asking not to wait
    /// is asking not to wait, rather than asking not to look. Delivery is a queue on somebody else's server, so the
    /// bound is what separates a slow relay from a message that will never arrive.
    /// </remarks>
    private async Task<string?> AwaitDeliveredMessageIdAsync(
        string marker,
        TimeSpan deliveryTimeout,
        CancellationToken cancellationToken)
    {
        var deadline = timeProvider.GetUtcNow() + deliveryTimeout;

        while (true)
        {
            if (await mailbox.FindDeliveredMessageIdAsync(marker, cancellationToken) is { } assigned)
            {
                return assigned;
            }

            if (timeProvider.GetUtcNow() >= deadline)
            {
                return null;
            }

            await Task.Delay(DeliveryPollInterval, timeProvider, cancellationToken);
        }
    }

    private static void AbandonRemainingTurns(
        IReadOnlyList<DeliverableTurn> turns,
        int failedTurn,
        List<DeliveryFailure> failures)
    {
        var failed = turns[failedTurn];

        foreach (var abandoned in turns.Skip(failedTurn + 1))
        {
            failures.Add(new DeliveryFailure(
                abandoned.MessageId,
                abandoned.Subject,
                $"not attempted: it answers <{failed.MessageId}>, which did not reach the mailbox"));
        }
    }
}
