// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Access.Sessions;

/// <summary>The row a mint or a renewal writes, composed by the caller that drew the token it names.</summary>
/// <param name="Identifier">The public half of the token, which the row is keyed by and which a request presents in the open.</param>
/// <param name="SecretDigest">The digest of the secret half, so what the deployment keeps opens nothing on its own.</param>
/// <param name="ExpiresAt">When presenting the token stops working, in UTC.</param>
/// <remarks>
/// It carries no grant, because the two writers establish one differently and neither lets the caller state it: a mint
/// takes the grant the exchange resolved, and a renewal takes the one the row it removes was holding. A shape carrying
/// a grant would let a renewal be asked to write a wider one than the session it replaces.
/// </remarks>
public sealed record MintedClientSessionRow(
    string Identifier,
    ReadOnlyMemory<byte> SecretDigest,
    DateTimeOffset ExpiresAt);
