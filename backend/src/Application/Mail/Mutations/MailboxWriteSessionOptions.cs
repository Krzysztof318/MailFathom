// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Mail.Mutations;

/// <summary>Bounds how long an account's single write connection outlives the mutations it carried.</summary>
public sealed class MailboxWriteSessionOptions
{
    /// <summary>Gets or sets how long the account's write connection is kept after its last session was disposed.</summary>
    /// <remarks>
    /// <para>
    /// The setting trades a handshake against a connection slot, and both sides of that trade are real. Closing the
    /// connection after every mutation would make a run of changes pay for a TCP connection, a TLS handshake, and an
    /// authentication each; holding it open indefinitely would spend one of the mail server's per-account connection
    /// slots on an account nobody is changing, which a provider limit or Dovecot's <c>mail_max_userip_connections</c>
    /// answers by refusing a login — surfacing as synchronization failing rather than as the write that caused it.
    /// </para>
    /// <para>
    /// It is an idle period rather than a lifetime: the clock restarts whenever a session is disposed, so a sequence of
    /// mutations keeps one connection for as long as it is being used and an account that stops changing gives its slot
    /// back a bounded time later.
    /// </para>
    /// </remarks>
    public TimeSpan ConnectionIdlePeriod { get; set; } = TimeSpan.FromMinutes(2);
}
