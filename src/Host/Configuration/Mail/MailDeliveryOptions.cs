// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using MailFathom.Domain.Delivery;

namespace MailFathom.Host.Configuration.Mail;

/// <summary>Configures how large a message this deployment is willing to compose and submit, and how it delivers one.</summary>
/// <remarks>
/// <para>
/// It is a section of its own rather than a block inside the synchronization settings, because it answers a question
/// about the whole deployment while the submission endpoint answers one about an account. Every account sends under the
/// same bounds — what a mailbox may send is a policy an operator holds once — and the endpoints they send through are
/// configured one at a time.
/// </para>
/// <para>
/// The delivery settings below are the same kind of decision: how much of one account's outbox a pass takes, how long
/// it holds it, and how patient this deployment is with a submission server that is not answering. None of them is a
/// per-account value either, because a provider that is briefly unreachable is answered the same way whichever mailbox
/// was waiting on it.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The options framework materializes this type during configuration binding.")]
internal sealed class MailDeliveryOptions : IValidatableObject
{
    /// <summary>The configuration section this deployment's sending bounds are read from.</summary>
    public const string SectionName = "MailDelivery";

    /// <summary>Gets or sets the greatest number of people one message may be addressed to.</summary>
    /// <remarks>
    /// The default is generous for correspondence and far below anything that reads as a mailing list, which this
    /// system refuses to be. The ceiling is what an outgoing record can hold, so a larger value would compose messages
    /// no record can be written for.
    /// </remarks>
    [Range(1, OutgoingEmailRequest.MaximumRecipientCount)]
    public int MaxRecipientCount { get; set; } = 50;

    /// <summary>Gets or sets the greatest number of characters either body of a message may carry.</summary>
    [Range(1, 10_000_000)]
    public int MaxBodyCharacters { get; set; } = 100_000;

    /// <summary>Gets or sets the greatest number of files one message may attach.</summary>
    [Range(0, 100)]
    public int MaxAttachmentCount { get; set; } = 10;

    /// <summary>Gets or sets the greatest number of octets one attached file may be made of.</summary>
    [Range(1, 100L * 1024 * 1024)]
    public long MaxAttachmentBytes { get; set; } = 10L * 1024 * 1024;

    /// <summary>Gets or sets the greatest number of octets the composed message may be transmitted as.</summary>
    /// <remarks>
    /// The default is the size most providers accept, and a deployment whose submission server accepts less is bounded
    /// by that server instead: what the server advertised is checked beside this number rather than in place of it.
    /// </remarks>
    [Range(1, 200L * 1024 * 1024)]
    public long MaxMessageBytes { get; set; } = 25L * 1024 * 1024;

    /// <summary>Gets or sets the greatest number of queued sends one delivery pass claims.</summary>
    /// <remarks>
    /// A send is a conversation with a submission server rather than a row to process, and a pass attempts them one at
    /// a time, so the useful values are small. What a pass leaves is claimed by the next one, oldest first.
    /// </remarks>
    [Range(1, 1000)]
    public int MaxDeliveriesPerPass { get; set; } = 10;

    /// <summary>Gets or sets how long a claim holds a queued send before another attempt may take it.</summary>
    /// <remarks>
    /// It is what makes a crash recoverable: a send in flight when a process stops is claimable again once this has
    /// passed, and nothing has to be told the process died. It never releases a send whose transmission had begun.
    /// </remarks>
    [Range(typeof(TimeSpan), "00:00:30", "01:00:00")]
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>Gets or sets how long one delivery attempt may run before it is cancelled.</summary>
    /// <remarks>
    /// It must stay below <see cref="LeaseDuration" />, and that ordering is the safety property rather than a
    /// preference: an attempt has to be cancelled before its lease can expire underneath it, because a lease that ran
    /// out while its holder was still transmitting is a second attempt taking a message the first may already have
    /// sent.
    /// </remarks>
    [Range(typeof(TimeSpan), "00:00:10", "00:59:00")]
    public TimeSpan AttemptTimeout { get; set; } = TimeSpan.FromMinutes(7);

    /// <summary>Gets or sets how many attempts one send may be handed out for before it stops being attempted.</summary>
    /// <remarks>
    /// A send that spends them all stands where an operator can see it rather than being retried forever. A value of
    /// <c>1</c> leaves no retry at all, so the first failure that could have cleared is terminal.
    /// </remarks>
    [Range(1, 100)]
    public int MaxAttempts { get; set; } = 5;

    /// <summary>Gets or sets the delay the first retry of a send is drawn around, from which the doubling grows.</summary>
    [Range(typeof(TimeSpan), "00:00:01", "01:00:00")]
    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>Gets or sets the ceiling a grown retry delay never exceeds.</summary>
    [Range(typeof(TimeSpan), "00:00:01", "24:00:00")]
    public TimeSpan RetryMaxDelay { get; set; } = TimeSpan.FromHours(1);

    /// <summary>Gets or sets how many accounts may be waiting for a prompt delivery pass at once.</summary>
    /// <remarks>
    /// The queue holds accounts rather than messages, and an account already waiting is not queued twice, so it cannot
    /// grow past the number of configured accounts however much is enqueued. Raising it past that buys nothing;
    /// lowering it below that means a signal is occasionally refused, which delays those sends until the account's own
    /// synchronization run drains them rather than losing any.
    /// </remarks>
    [Range(1, 1000)]
    public int SignalQueueCapacity { get; set; } = 64;

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        // A message carries its attachments, their transfer encoding, and its own headers, so a per-file bound above
        // the whole-message bound describes a file that could never be sent — and the refusal an operator would meet is
        // the one about the message rather than the one they configured.
        if (this.MaxAttachmentCount > 0 && this.MaxAttachmentBytes > this.MaxMessageBytes)
        {
            yield return new ValidationResult(
                "MaxAttachmentBytes must not exceed MaxMessageBytes, because an attachment is transmitted inside the message that carries it.",
                [nameof(this.MaxAttachmentBytes)]);
        }

        // Refused rather than warned about: an attempt that outlives its lease is a second attempt taking a message the
        // first may already have transmitted, and the only thing standing between the two is this ordering.
        if (this.AttemptTimeout >= this.LeaseDuration)
        {
            yield return new ValidationResult(
                "AttemptTimeout must be shorter than LeaseDuration, so a delivery attempt is cancelled before its lease can expire and let a second attempt take the same message.",
                [nameof(this.AttemptTimeout)]);
        }

        if (this.RetryMaxDelay < this.RetryBaseDelay)
        {
            yield return new ValidationResult(
                "RetryMaxDelay must not be below RetryBaseDelay, because it is the ceiling the growing delay is capped at.",
                [nameof(this.RetryMaxDelay)]);
        }
    }
}
