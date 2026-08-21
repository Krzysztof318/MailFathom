// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailKit.Net.Smtp;

namespace MailFathom.Infrastructure.Mail.MailKit.Delivery;

/// <summary>Decides what a submission server's refusal means, from its reply code and the enhanced code beside it.</summary>
/// <remarks>
/// <para>
/// It lives beside the session that reads the reply rather than with the general failure classification, because the
/// two questions have different answers. This one says what the server stated; the resilience pipeline asks whether an
/// operation may be repeated, and it reaches that answer through here so that both readings of one reply can never
/// disagree.
/// </para>
/// <para>
/// Nothing but numbers leaves this type. The reply text is examined for the enhanced status code at the front of it and
/// is otherwise not read, kept, or passed on, so a classification carries no address, no message, and nothing the
/// server wrote.
/// </para>
/// </remarks>
internal static class SmtpReplyClassifier
{
    /// <summary>The reply class RFC 5321 defines as a temporary negative completion.</summary>
    private const int TemporaryReplyClass = 4;

    /// <summary>The enhanced status class RFC 3463 defines as a permanent failure.</summary>
    private const int PermanentEnhancedClass = 5;

    /// <summary>Classifies the refusal a submission server answered a command with.</summary>
    /// <param name="rejection">The refusal the mail library raised.</param>
    /// <returns>The reply code, the enhanced status code where the server sent one, and what the refusal means.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="rejection" /> is <see langword="null" />.</exception>
    internal static SmtpReplyClassification Classify(SmtpCommandException rejection)
    {
        ArgumentNullException.ThrowIfNull(rejection);

        return Classify((int)rejection.StatusCode, rejection.Message);
    }

    /// <summary>Classifies one reply from its code and the text that followed it.</summary>
    /// <param name="replyCode">The three-digit reply code.</param>
    /// <param name="replyText">The reply text as the server sent it, read for its enhanced status code alone.</param>
    /// <returns>The reply code, the enhanced status code where the reply opened with one, and what the refusal means.</returns>
    /// <remarks>
    /// <para>
    /// The reply code decides, because RFC 5321 makes its first digit the statement a client is entitled to act on: a
    /// 4yz reply is the server saying it did not take the command and that returning is welcome, and everything else
    /// is treated as settled — including a reply this system does not recognize, since repeating a submission nobody
    /// understood is what puts a second copy in a mailbox.
    /// </para>
    /// <para>
    /// The enhanced status code refines that in one direction only. RFC 3463 asks a server to keep the two consistent
    /// and servers do disagree with themselves, so where the enhanced class says permanent over a 4yz reply the
    /// permanent reading wins, and an enhanced class that merely agrees, contradicts in the safe direction, or reports
    /// success in a refusal changes nothing. The asymmetry is deliberate: being wrong about a permanent failure costs
    /// a delivery that had already failed, and being wrong about a transient one costs a message somebody receives
    /// twice.
    /// </para>
    /// </remarks>
    internal static SmtpReplyClassification Classify(int replyCode, string? replyText)
    {
        var enhancedStatusCode = SmtpEnhancedStatusCode.TryParse(replyText, out var parsedEnhancedStatusCode)
            ? parsedEnhancedStatusCode
            : null;

        return new SmtpReplyClassification(
            replyCode,
            enhancedStatusCode,
            DecideDisposition(replyCode, enhancedStatusCode));
    }

    private static SmtpRejectionDisposition DecideDisposition(int replyCode, SmtpEnhancedStatusCode? enhancedStatusCode)
    {
        var statedByReplyCode = replyCode / 100 == TemporaryReplyClass
            ? SmtpRejectionDisposition.Transient
            : SmtpRejectionDisposition.Permanent;

        return enhancedStatusCode?.Class == PermanentEnhancedClass
            ? SmtpRejectionDisposition.Permanent
            : statedByReplyCode;
    }
}
