// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;

namespace MailFathom.Application.Access.Sessions;

/// <summary>What a signed-in client's session admits, which is what the exchange that minted it established.</summary>
/// <param name="User">The user the request acts for, which is never absent and is what an erasure reaches the session through.</param>
/// <param name="CredentialId">The credential the exchange authenticated, or <see langword="null" /> where a client endpoint requiring no credential answered it.</param>
/// <param name="Permissions">The names the session's requests are kept to, in the published order: the narrowing of the credential the exchange authenticated, never the grant itself.</param>
/// <remarks>
/// <para>
/// The three facts travel together because a renewal carries all three forward unchanged. What a request holds is its
/// user's grant, read from the user's roles on every request and kept to these names, so a role assigned or revoked
/// reaches an open session at its next request rather than at its next sign-in.
/// </para>
/// <para>
/// The credential is absent rather than a sentinel, because the column holding it is a foreign key: a client endpoint
/// requiring no credential still answers the exchange, and a magic identifier matching no row cannot survive that
/// constraint. The user is never absent, which is what lets an erasure remove such a session by cascade when no
/// credential stands behind it to cascade through.
/// </para>
/// </remarks>
public sealed record ClientSessionGrant(
    UserId User,
    Guid? CredentialId,
    IReadOnlyList<MailFathomPermission> Permissions);
