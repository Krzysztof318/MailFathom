// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Host.Signals;

/// <summary>What a minting route answers a client with.</summary>
/// <param name="Value">The whole ticket, which is the only form the client ever sees.</param>
/// <param name="ExpiresAt">When presenting it stops working, so a client that could not connect knows to mint another rather than retrying this one.</param>
internal sealed record MintedClientSignalTicket(string Value, DateTimeOffset ExpiresAt);
