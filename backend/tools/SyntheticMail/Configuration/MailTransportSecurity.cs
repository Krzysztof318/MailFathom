// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.SyntheticMail.Configuration;

/// <summary>How the connection carrying a credential is secured, for either of the two protocols this tool speaks.</summary>
/// <remarks>
/// Two values secure the connection, because there are two ways to secure an SMTP submission or an IMAP session, and
/// both connections authenticate with a password. <see cref="Unsecured" /> is the third and is bounded rather than
/// offered: it exists for a throwaway mail server running on the same machine, and the reader refuses it against any
/// host that is not one. The conventional port differs per protocol and is resolved where an account is read rather
/// than here, because a value naming a security is not a value naming a port.
/// </remarks>
internal enum MailTransportSecurity
{
    /// <summary>Connect in the clear on the protocol's plain port and require the server to upgrade with <c>STARTTLS</c>, usually 587 for submission and 143 for IMAP.</summary>
    /// <remarks>Required rather than opportunistic: a server that does not advertise the extension fails the connection instead of continuing unencrypted.</remarks>
    StartTls = 0,

    /// <summary>Handshake TLS immediately on connecting, usually 465 for submission and 993 for IMAP.</summary>
    ImplicitTls = 1,

    /// <summary>Connect in the clear and stay there, on 25 for submission and 143 for IMAP.</summary>
    /// <remarks>
    /// This puts the password on the wire, and choosing it therefore also permits clear-text authentication — there is
    /// no second switch, because a plain connection whose credential could not be presented in the clear would reach
    /// nothing at all. The one case it exists for is a local test mail server: a container produces the same corpus
    /// twice and belongs to nobody, where the alternative is a real mailbox with a real credential a pipeline would
    /// have to hold. That is why the reader refuses this value against a host that is neither loopback nor a container
    /// address, rather than trusting the name to keep it local.
    /// </remarks>
    Unsecured = 2,
}
