// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.SyntheticMail.Configuration;
using MailFathom.SyntheticMail.Generation;
using MimeKit;

namespace MailFathom.SyntheticMail.Delivery;

/// <summary>Submits a generated corpus one message at a time, paced, and reports what became of it.</summary>
/// <param name="transport">The open session to submit through.</param>
/// <param name="timeProvider">What the pacing waits on.</param>
/// <remarks>
/// A message is composed immediately before it is submitted and disposed immediately after, so a batch's peak memory
/// is one message rather than all of them — which is what lets the attachment ceiling be raised without the count
/// having to come down to match.
/// </remarks>
internal sealed class SyntheticMailBatchDelivery(ISyntheticMailTransport transport, TimeProvider timeProvider)
{
    /// <summary>Delivers the whole corpus, continuing past a message the server refuses.</summary>
    /// <param name="emails">The generated corpus, oldest first.</param>
    /// <param name="account">The account being submitted as, which decides the author of each message.</param>
    /// <param name="recipient">The one real address every message is delivered to.</param>
    /// <param name="interval">How long to wait between two submissions.</param>
    /// <param name="cancellationToken">Cancels the batch, which stops it rather than recording a failure.</param>
    /// <returns>What was delivered and what was not.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    internal async Task<DeliveryReport> DeliverAsync(
        IReadOnlyList<SyntheticEmail> emails,
        SendingAccount account,
        MailboxAddress recipient,
        TimeSpan interval,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(emails);
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(recipient);

        var failures = new List<DeliveryFailure>();
        var delivered = 0;

        for (var index = 0; index < emails.Count; index++)
        {
            // Between messages rather than before each, so a batch of one is immediate and a batch of a thousand is
            // spread. A real submission server rate-limits, and a burst is answered with a refusal that says nothing
            // about the mail.
            if (index > 0 && interval > TimeSpan.Zero)
            {
                await Task.Delay(interval, timeProvider, cancellationToken);
            }

            var email = emails[index];

            using var message = SyntheticMimeComposer.Compose(
                email,
                recipient,
                account.Address,
                account.AuthorIdentity);

            try
            {
                await transport.SendAsync(message, recipient, cancellationToken);
                delivered++;
            }
            catch (SyntheticMailFailure failure)
            {
                failures.Add(new DeliveryFailure(email.MessageId, email.Subject, failure.Message));
            }
        }

        return new DeliveryReport(emails.Count, delivered, failures);
    }
}
