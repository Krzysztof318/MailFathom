// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.SyntheticMail.UnitTests.TestDoubles;

/// <summary>One submission, as it was at the moment it was made.</summary>
/// <param name="MessageId">The identity the corpus gave the message.</param>
/// <param name="Subject">Its subject.</param>
/// <param name="EnvelopeRecipients">Who the transport was told to deliver it to, which is not what the headers say.</param>
/// <param name="From">The <c>From</c> header.</param>
/// <param name="Sender">The <c>Sender</c> header, or <see langword="null" /> when there is none.</param>
/// <param name="ReplyTo">The <c>Reply-To</c> header.</param>
/// <param name="To">The <c>To</c> header.</param>
/// <param name="Cc">The <c>Cc</c> header.</param>
/// <param name="InReplyTo">The <c>In-Reply-To</c> header, or <see langword="null" /> when the message opens a thread.</param>
/// <param name="References">The <c>References</c> header, oldest first.</param>
/// <param name="Marker">The value of the header an exchange stamps a submission with, or <see langword="null" /> on a message no exchange submitted.</param>
/// <remarks>
/// A snapshot rather than the message itself, because the batch disposes each message immediately after submitting it:
/// a double that kept the reference would hand every assertion a disposed object.
/// </remarks>
internal sealed record SubmittedMessage(
    string MessageId,
    string Subject,
    IReadOnlyList<string> EnvelopeRecipients,
    IReadOnlyList<string> From,
    string? Sender,
    IReadOnlyList<string> ReplyTo,
    IReadOnlyList<string> To,
    IReadOnlyList<string> Cc,
    string? InReplyTo,
    IReadOnlyList<string> References,
    string? Marker);
