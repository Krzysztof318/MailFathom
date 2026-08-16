// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailKit.Net.Smtp;

namespace MailFathom.Infrastructure.Mail.MailKit.Delivery;

/// <summary>Creates every SMTP client this deployment opens, and is the one place a protocol logger could be attached.</summary>
/// <remarks>
/// <para>
/// It exists for the reason its IMAP counterpart does. MailKit writes the bytes of a session — every command, every
/// response, and the message inside them — to the protocol logger it was constructed with, and to nothing else. A
/// client built with none writes them nowhere, which is what makes "no mail reaches a log or an exporter through the
/// mail library" a property of construction rather than of a level or a configuration key somebody could set.
/// </para>
/// <para>
/// A submission session carries the whole outgoing message, so what such a logger would publish here is the mail this
/// deployment sends, in full, including its recipients.
/// </para>
/// </remarks>
internal static class MailKitSmtpClientFactory
{
    /// <summary>Creates an SMTP client that writes no protocol traffic anywhere.</summary>
    /// <returns>A disconnected client, owned by the caller.</returns>
    internal static SmtpClient CreateWithoutProtocolLogging() => new();
}
