// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using MailFathom.SyntheticMail.Configuration;
using MailFathom.SyntheticMail.Generation;
using MimeKit;

namespace MailFathom.SyntheticMail.Delivery;

/// <summary>Delivers generated exchanges one turn at a time, so each reply answers the message that actually arrived.</summary>
/// <param name="transport">The open submission session the correspondent's half is sent through.</param>
/// <param name="mailbox">The open session against the mailbox MailFathom synchronizes.</param>
/// <param name="console">Where the run reports what it is doing, which is standard error and never the corpus.</param>
/// <param name="timeProvider">What the pacing and the delivery wait run on.</param>
/// <remarks>
/// <para>
/// This is the part a flat batch cannot do. A batch composes every message from identifiers it invented and submits
/// them in any order, so its threading holds only while the submission server leaves <c>Message-Id</c> alone. An
/// exchange submits one turn, waits for the copy to appear in the mailbox, reads the identifier the server assigned
/// to it, and builds the next turn's ancestry from that — which is why the ancestry the generator produced is
/// replaced here rather than composed as it stands.
/// </para>
/// <para>
/// The two sides reach the mailbox by different routes, and both routes end in it. The correspondent's half is
/// submitted by the sending account and delivered; the mailbox's own half is appended to its Sent folder, which is
/// where a mail client puts what it wrote. Both halves therefore synchronize, and a thread reads as message, reply,
/// message, reply rather than as a run of inbound mail.
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
    /// <param name="conversations">The generated exchanges, each oldest message first.</param>
    /// <param name="account">The account being submitted as, which decides the author of the correspondent's half.</param>
    /// <param name="watchedMailbox">The mailbox MailFathom synchronizes, which every exchange is with.</param>
    /// <param name="interval">How long to wait between two submissions.</param>
    /// <param name="deliveryTimeout">How long to wait for a submitted message to appear in the mailbox.</param>
    /// <param name="cancellationToken">Cancels the run, which stops it rather than recording a failure.</param>
    /// <returns>What was delivered and what was not.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    internal async Task<DeliveryReport> DeliverAsync(
        IReadOnlyList<SyntheticConversation> conversations,
        SendingAccount account,
        MailboxAddress watchedMailbox,
        TimeSpan interval,
        TimeSpan deliveryTimeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(conversations);
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(watchedMailbox);

        var failures = new List<DeliveryFailure>();
        var attempted = 0;
        var delivered = 0;

        for (var index = 0; index < conversations.Count; index++)
        {
            var conversation = conversations[index];

            var deliveredTurns = await this.DeliverConversationAsync(
                conversation,
                account,
                watchedMailbox,
                interval,
                deliveryTimeout,
                attempted > 0,
                failures,
                string.Create(CultureInfo.InvariantCulture, $"Exchange {index + 1} of {conversations.Count}"),
                cancellationToken);

            attempted += conversation.Messages.Count;
            delivered += deliveredTurns;
        }

        return new DeliveryReport(attempted, delivered, failures);
    }

    /// <summary>Delivers one exchange, and abandons the rest of it as soon as a turn fails.</summary>
    /// <remarks>
    /// Abandoned rather than continued, because every remaining turn answers the one that failed: a reply built on an
    /// identifier that was never assigned is exactly the broken threading an exchange exists to replace. The
    /// abandoned turns are still reported, so the count a run prints stays the count the seed described.
    /// </remarks>
    private async Task<int> DeliverConversationAsync(
        SyntheticConversation conversation,
        SendingAccount account,
        MailboxAddress watchedMailbox,
        TimeSpan interval,
        TimeSpan deliveryTimeout,
        bool paceFirstTurn,
        List<DeliveryFailure> failures,
        string exchange,
        CancellationToken cancellationToken)
    {
        var correspondent = new MailboxAddress(
            conversation.Correspondent.DisplayName,
            conversation.Correspondent.Address);

        // Whichever address the correspondent's half was authored from is the one the mailbox writes back to, so a
        // reply is addressed to the person a reader would have replied to rather than to a participant the inbound
        // message only mentioned.
        var repliedTo = account.AuthorIdentity == SyntheticAuthorIdentity.Fabricated ? correspondent : account.Address;

        // Every identifier the mailbox has actually assigned in this exchange, oldest first. It is both halves of the
        // ancestry a reply carries: the whole list is its `References` and the last entry is its `In-Reply-To`.
        var ancestry = new List<string>();
        var delivered = 0;

        for (var turn = 0; turn < conversation.Messages.Count; turn++)
        {
            if ((turn > 0 || paceFirstTurn) && interval > TimeSpan.Zero)
            {
                await Task.Delay(interval, timeProvider, cancellationToken);
            }

            var email = conversation.Messages[turn] with
            {
                InReplyTo = ancestry.Count == 0 ? null : ancestry[^1],
                References = [.. ancestry],
            };

            var assignedMessageId = await this.DeliverTurnAsync(
                email,
                SyntheticConversation.SideOf(turn),
                account,
                watchedMailbox,
                repliedTo,
                deliveryTimeout,
                failures,
                string.Create(CultureInfo.InvariantCulture, $"{exchange}, turn {turn + 1} of {conversation.Messages.Count}"),
                cancellationToken);

            if (assignedMessageId is null)
            {
                AbandonRemainingTurns(conversation, turn, failures);

                return delivered;
            }

            ancestry.Add(assignedMessageId);
            delivered++;
        }

        return delivered;
    }

    /// <summary>Delivers one turn and reports the identifier the mailbox now holds it under, or nothing when it failed.</summary>
    private async Task<string?> DeliverTurnAsync(
        SyntheticEmail email,
        SyntheticThreadSide side,
        SendingAccount account,
        MailboxAddress watchedMailbox,
        MailboxAddress repliedTo,
        TimeSpan deliveryTimeout,
        List<DeliveryFailure> failures,
        string turn,
        CancellationToken cancellationToken)
    {
        try
        {
            if (side == SyntheticThreadSide.Mailbox)
            {
                console.WriteError($"{turn}: appending to Sent.");

                using var written = SyntheticMimeComposer.ComposeFromMailbox(email, watchedMailbox, repliedTo);

                await mailbox.AppendToSentAsync(written, cancellationToken);

                console.WriteError($"{turn}: appended.");

                // The appended copy is the one this run composed, so nothing rewrote its identifier on the way in and
                // there is nothing to read back.
                return email.MessageId;
            }

            console.WriteError($"{turn}: submitting to {watchedMailbox.Address}.");

            using var submitted = SyntheticMimeComposer.Compose(
                email,
                watchedMailbox,
                account.Address,
                account.AuthorIdentity);

            SyntheticDeliveryMarker.Stamp(submitted, email.MessageId);

            await transport.SendAsync(submitted, watchedMailbox, cancellationToken);

            console.WriteError(string.Create(
                CultureInfo.InvariantCulture,
                $"{turn}: waiting up to {deliveryTimeout.TotalSeconds:0} seconds for it to reach the mailbox."));

            var assigned = await this.AwaitDeliveredMessageIdAsync(email.MessageId, deliveryTimeout, cancellationToken);

            console.WriteError(assigned is null ? $"{turn}: never arrived." : $"{turn}: delivered.");

            if (assigned is null)
            {
                failures.Add(new DeliveryFailure(
                    email.MessageId,
                    email.Subject,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"submitted, but no copy of it reached the mailbox within {deliveryTimeout.TotalSeconds:0} seconds")));
            }

            return assigned;
        }
        catch (SyntheticMailFailure failure)
        {
            console.WriteError($"{turn}: failed.");
            failures.Add(new DeliveryFailure(email.MessageId, email.Subject, failure.Message));

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
        SyntheticConversation conversation,
        int failedTurn,
        List<DeliveryFailure> failures)
    {
        var failed = conversation.Messages[failedTurn];

        foreach (var abandoned in conversation.Messages.Skip(failedTurn + 1))
        {
            failures.Add(new DeliveryFailure(
                abandoned.MessageId,
                abandoned.Subject,
                $"not attempted: it answers <{failed.MessageId}>, which did not reach the mailbox"));
        }
    }
}
