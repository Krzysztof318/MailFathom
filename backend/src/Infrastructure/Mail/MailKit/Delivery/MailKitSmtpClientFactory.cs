// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

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
    /// <remarks>
    /// It is MailFathom's own subclass rather than the library's client, because a partial acceptance has to be
    /// recorded rather than raised on. What the subclass changes is stated where it is declared. The concrete type is
    /// what is returned rather than <see cref="ISubmissionClient" />, so the invariant this factory exists for stays
    /// assertable: the protocol logger a client was constructed with is a property of the library's client class and
    /// of no interface it implements. A caller that wants the contract converts the method group, which delegate
    /// return-type covariance already permits.
    /// </remarks>
    internal static SubmissionSmtpClient CreateWithoutProtocolLogging() => new();
}
