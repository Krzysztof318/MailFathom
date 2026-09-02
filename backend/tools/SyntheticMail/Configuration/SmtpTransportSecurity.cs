// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.SyntheticMail.Configuration;

/// <summary>How the connection carrying the credential is secured.</summary>
/// <remarks>
/// There are two values because there are two ways to secure an SMTP submission, and there is no third because a
/// clear-text one is what this tool must never do: it authenticates with a password, so an unsecured connection would
/// put that password on the wire. Refusing the downgrade is left to the type rather than to a check somebody could
/// forget — an endpoint that cannot do either of these fails the connection, which is the intended outcome.
/// </remarks>
internal enum SmtpTransportSecurity
{
    /// <summary>Connect in the clear on the submission port and require the server to upgrade with <c>STARTTLS</c>, usually 587.</summary>
    /// <remarks>Required rather than opportunistic: a server that does not advertise the extension fails the connection instead of continuing unencrypted.</remarks>
    StartTls = 0,

    /// <summary>Handshake TLS immediately on connecting, usually 465.</summary>
    ImplicitTls = 1,
}
