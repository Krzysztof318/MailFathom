// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailKit.Net.Imap;

namespace MailFathom.Infrastructure.Mail.MailKit;

/// <summary>Creates every IMAP client this deployment opens, and is the one place a protocol logger could be attached.</summary>
/// <remarks>
/// <para>
/// MailKit writes the bytes of a session — every command, every response, and the envelopes and payloads inside them —
/// to the protocol logger it was constructed with, and to nothing else. A client built with none writes them nowhere,
/// which is what makes "no mail reaches a log or an exporter through the mail library" a property of construction
/// rather than of a level, a category filter, or a configuration key somebody could set. There is no such key, and this
/// type is why one could not be honoured without passing through the test that asserts the opposite.
/// </para>
/// <para>
/// It exists for that invariant alone. A client needs no other arrangement at construction — its timeouts, its
/// transport security, and its authentication are settled by the connection that opens it — so a second reason to route
/// construction through here would be a reason to question this one.
/// </para>
/// </remarks>
internal static class MailKitImapClientFactory
{
    /// <summary>Creates an IMAP client that writes no protocol traffic anywhere.</summary>
    /// <returns>A disconnected client, owned by the caller.</returns>
    internal static ImapClient CreateWithoutProtocolLogging() => new();
}
