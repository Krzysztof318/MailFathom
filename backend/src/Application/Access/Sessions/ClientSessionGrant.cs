// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;

namespace MailFathom.Application.Access.Sessions;

/// <summary>What a signed-in client's session admits, which is what the exchange that minted it established.</summary>
/// <param name="User">The user the request acts for, which is never absent and is what an erasure reaches the session through.</param>
/// <param name="CredentialId">The credential the exchange authenticated, or <see langword="null" /> where a client endpoint requiring no credential answered it.</param>
/// <param name="Permissions">What the session's requests may do, in the published order.</param>
/// <remarks>
/// <para>
/// The three facts travel together because a renewal carries all three forward unchanged: what a session admits is what
/// the credential admitted at the sign-in, so a grant narrowed afterwards reaches somebody at their next sign-in rather
/// than at their next renewal.
/// </para>
/// <para>
/// The credential is absent rather than a sentinel, because the column holding it is a foreign key: a client endpoint
/// requiring no credential still answers the exchange, and a magic identifier matching no row cannot survive that
/// constraint. The user is never absent, which is what lets an erasure remove such a session by cascade when no
/// credential stands behind it to cascade through.
/// </para>
/// </remarks>
public sealed record ClientSessionGrant(
    MailUserId User,
    Guid? CredentialId,
    IReadOnlyList<MailFathomPermission> Permissions);
