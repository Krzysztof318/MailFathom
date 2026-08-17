// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using MailFathom.Domain.Delivery;

namespace MailFathom.Host.Configuration.Mail;

/// <summary>Configures how large a message this deployment is willing to compose and submit.</summary>
/// <remarks>
/// It is a section of its own rather than a block inside the synchronization settings, because it answers a question
/// about the whole deployment while the submission endpoint answers one about an account. Every account sends under the
/// same bounds — what a mailbox may send is a policy an operator holds once — and the endpoints they send through are
/// configured one at a time.
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
    }
}
