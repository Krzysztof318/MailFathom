// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Domain.Access;
using MailFathom.TestSupport;

namespace MailFathom.Host.UnitTests.TestDoubles;

/// <summary>The grant an endpoint test arranges when the grant is not what that test is about.</summary>
/// <remarks>
/// An endpoint test's subject is what the route decides about a request — an account it does not serve, a page size out
/// of range, a cursor issued for other filters — and every one of those is reached through a use case that now asks what
/// the caller holds. Granting the whole surface keeps those tests about the shapes they were written for. Which
/// permission each route is published under is asserted in <c>AdminApiEndpointsTests</c>, and what each use case refuses
/// without is asserted where that use case lives, so nothing is left unproven by arranging a caller who holds all of it.
/// </remarks>
internal static class AdministrativeGrant
{
    /// <summary>Gets the authorization of a caller granted every permission this surface publishes.</summary>
    internal static AccessAuthorization WholeSurface { get; } = AccessAuthorizations.ForCallerGranted(
        [.. MailFathomPermission.All.Where(permission => permission.Surface == ProtectedSurface.Administration)]);
}
