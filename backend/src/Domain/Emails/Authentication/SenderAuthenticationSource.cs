// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Emails.Authentication;

/// <summary>Who reached a message's sender-authentication verdict.</summary>
/// <remarks>
/// <para>
/// The two are not interchangeable and the difference cannot be inferred from anything else on the row. A receiving
/// server saw the connection the message arrived on, so it could evaluate SPF against the connecting address and read
/// an envelope sender this process never had. MailFathom, verifying after delivery, has the signed bytes and a
/// published key and nothing else — which is enough for a cryptographic identity and for nothing that depends on the
/// transport.
/// </para>
/// <para>
/// It is recorded because an account's configuration may change afterwards. A reader meeting a verdict months later
/// cannot work out from the trusted authority in force today which of the two produced the row in front of them, and
/// a cryptographic signature check and a verdict taken with network context nobody has any more are worth different
/// amounts.
/// </para>
/// </remarks>
public enum SenderAuthenticationSource
{
    /// <summary>The verdict is what the account's trusted receiving server wrote, read back out of its header.</summary>
    /// <remarks>
    /// It is also what a message carries when nothing was read at all — no trusted authority, no header bearing its
    /// identifier, and local verification switched off. The value names which reading produced the verdict rather than
    /// asserting that a server established something, so a not-established verdict reached this way carries it too.
    /// </remarks>
    ReceivingServer = 0,

    /// <summary>MailFathom verified the message's own DKIM signatures, because no trusted server statement was available.</summary>
    /// <remarks>
    /// Reached only as a fallback. An account whose receiving server writes the header goes on believing that server,
    /// so the two never sit beside each other on one message. Everything such a verdict names came from a signature in
    /// the stored bytes and a key the signing domain publishes: nothing about the envelope, the connecting address, or
    /// the sender's DMARC policy is knowable here, and the columns holding those record that they were not evaluated.
    /// </remarks>
    LocalVerification = 1,
}
