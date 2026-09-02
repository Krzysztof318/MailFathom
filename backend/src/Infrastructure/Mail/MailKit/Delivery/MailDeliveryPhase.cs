// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Infrastructure.Mail.MailKit.Delivery;

/// <summary>Names one stage of reaching a submission server that carries a time budget of its own.</summary>
/// <remarks>
/// The stages are separated because they fail for different reasons and an operator acts on each differently. A
/// connection that never opens is a firewall, a route, or a wrong port; a greeting that never arrives is a server
/// listening without answering, or a TLS handshake nobody completes; an authentication that hangs is the credential
/// path; and a command that stops answering is a session that has already been established. One budget across all four
/// would report every one of them as the same wait.
/// </remarks>
internal enum MailDeliveryPhase
{
    /// <summary>Opening the transport to the submission endpoint, before a byte of the protocol is spoken.</summary>
    Connection = 0,

    /// <summary>Negotiating encryption where the mode calls for it, reading the server's greeting, and exchanging capabilities.</summary>
    Greeting = 1,

    /// <summary>Presenting the account's credential and receiving the server's verdict on it.</summary>
    Authentication = 2,

    /// <summary>Every command issued over the established session, which the client bounds for itself.</summary>
    Command = 3,

    /// <summary>Offering the envelope and transmitting the message, from the first command of the submission to the server's answer to the body.</summary>
    /// <remarks>
    /// It is separated from every other command because it is the one that cannot be repeated: a budget that expires
    /// here leaves a message that may already have reached its recipients, and the record the send is written on has to
    /// be able to say that rather than reading it as one more command that timed out.
    /// </remarks>
    Transmission = 4,
}
