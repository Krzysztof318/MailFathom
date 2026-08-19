// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Emails.Authentication;
using MimeKit;

namespace MailFathom.Infrastructure.Mail.Dkim;

/// <summary>Establishes what a delivered message's own bytes still prove about its sender.</summary>
/// <remarks>
/// <para>
/// It exists as a seam inside the parsing adapter rather than as a decorator above it, because verification needs the
/// parsed message and nothing above the port has one: reaching it any higher would mean parsing the same bytes twice
/// to answer one question.
/// </para>
/// <para>
/// An implementation is a fallback and is asked only where no trusted server statement was found. It answers with a
/// verdict in every case, including the cases where it establishes nothing, and it raises for nothing a mailbox can
/// contain — a message whose signature is malformed, whose key is unpublished, or whose nameserver will not answer is
/// an ordinary message with an ordinary verdict.
/// </para>
/// </remarks>
internal interface ILocalSenderVerifier
{
    /// <summary>Verifies what the message itself carries about who sent it.</summary>
    /// <param name="message">The parsed message, whose content the verification reads.</param>
    /// <param name="displayedSenderAddress">The address the message's <c>From</c> header wrote, where it wrote one.</param>
    /// <param name="cancellationToken">Cancels the verification and the lookups it makes.</param>
    /// <returns>The verdict, which names <see cref="SenderAuthenticationSource.LocalVerification" /> whatever it says.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="message" /> is <see langword="null" />.</exception>
    Task<SenderAuthentication> VerifyAsync(
        MimeMessage message,
        string? displayedSenderAddress,
        CancellationToken cancellationToken);
}
