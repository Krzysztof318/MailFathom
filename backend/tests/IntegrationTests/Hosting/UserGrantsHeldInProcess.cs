// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Access.Grants;
using MailFathom.Domain.Access;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;

namespace MailFathom.IntegrationTests.Hosting;

/// <summary>Stands in for the role assignments a user's grant is computed from, in a shape that reaches no database.</summary>
/// <remarks>
/// <para>
/// What a caller may do is its user's grant narrowed by the credential that admitted it, and the user's half is read
/// from the records for every authenticated request. A shape that substitutes its credential rows therefore substitutes
/// this beside them: left to the deployment's own store, the first request a credential admits reaches a database the
/// shape has not got and is answered as a fault rather than by the route it named.
/// </para>
/// <para>
/// Every user is given every permission at the deployment's scope, which is the one answer that leaves a shape's
/// credentials deciding what a request reaches — the narrowing a shape states on a key still narrows, and nothing here
/// widens a credential past what it lists.
/// </para>
/// </remarks>
internal static class UserGrantsHeldInProcess
{
    /// <summary>Replaces the grant store with one answering that every user holds everything.</summary>
    /// <param name="services">The shape's registrations, after the composition has run.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services" /> is <see langword="null" />.</exception>
    internal static void GrantEveryUserEverything(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Composed once rather than per scope, for the reason ProvisionedUserApiKeys gives about its own substitute.
        var grants = Substitute.For<IGrantStore>();

        grants.ReadGrantOfAsync(Arg.Any<UserId>(), Arg.Any<CancellationToken>())
            .Returns(ScopedGrant.AtDeployment(MailFathomPermission.All));

        services.RemoveAll<IGrantStore>();
        services.AddSingleton(grants);
    }
}
