// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Access.Sessions;

/// <summary>One session the deployment is holding, as the row naming it reports it.</summary>
/// <param name="Grant">What the session admits, which the exchange established and a renewal carries forward.</param>
/// <param name="SecretDigest">The digest of the secret half, which the presented secret is compared against in constant time.</param>
/// <param name="ExpiresAt">When presenting the token stops working, in UTC.</param>
/// <remarks>
/// The digest crosses the port rather than the comparison, for the reason the signal ticket's does: the store answers
/// what it holds, and whether a presented value proves it is decided by the type that also decides what a malformed
/// value is. A row read out of the database, a dump, or a backup is therefore not a session.
/// </remarks>
public sealed record HeldClientSession(
    ClientSessionGrant Grant,
    ReadOnlyMemory<byte> SecretDigest,
    DateTimeOffset ExpiresAt);
