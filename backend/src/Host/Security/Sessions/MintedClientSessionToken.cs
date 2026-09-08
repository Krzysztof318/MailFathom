// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Host.Security.Sessions;

/// <summary>What the exchange answers a client with.</summary>
/// <param name="Value">The whole token, which is the only form the client ever sees and the value it presents afterwards.</param>
/// <param name="ExpiresAt">When presenting it stops working, so a client renews before that rather than discovering it by being refused.</param>
internal sealed record MintedClientSessionToken(string Value, DateTimeOffset ExpiresAt);
