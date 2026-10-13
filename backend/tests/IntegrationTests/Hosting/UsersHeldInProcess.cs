// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Access.Grants;
using MailFathom.Domain.Access;
using MailFathom.Host.Configuration.UserSettings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using NSubstitute;

namespace MailFathom.IntegrationTests.Hosting;

/// <summary>Stands in for the rows a request reads about the user it acts for, in a shape that reaches no database.</summary>
/// <remarks>
/// <para>
/// A credential names a user and the pipeline then reads two things about them before any route runs: the record they
/// are served from, and the grant their role assignments give them. Both are rows. A shape that substitutes its
/// credential rows therefore substitutes these beside them — left to the deployment's own stores, the first request a
/// credential admits reaches a database the shape has not got and is answered as a fault rather than by the route it
/// named.
/// </para>
/// <para>
/// The record is not substituted at all: each user is handed to <see cref="ServedUsers" /> as one this replica already
/// composed, which is the state a deployment reaches after the first request for them, and a held user is answered
/// without a read. They are served no mailbox, since a shape reaches no mail. The grant is every permission at the
/// deployment's scope, which is the one answer that leaves a shape's credentials deciding what a request reaches — the
/// narrowing a shape states on a key still narrows, and nothing here widens a credential past what it lists.
/// </para>
/// </remarks>
internal static class UsersHeldInProcess
{
    /// <summary>Holds the named users as served and answers that each of them holds everything.</summary>
    /// <param name="services">The shape's registrations, after the composition has run.</param>
    /// <param name="users">The users the shape's credentials name.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    internal static void Hold(IServiceCollection services, IReadOnlyCollection<UserId> users)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(users);

        // Composed once rather than per scope, for the reason ProvisionedUserApiKeys gives about its own substitute.
        var grants = Substitute.For<IGrantStore>();

        grants.ReadGrantOfAsync(Arg.Any<UserId>(), Arg.Any<CancellationToken>())
            .Returns(ScopedGrant.AtDeployment(MailFathomPermission.All));

        services.RemoveAll<IGrantStore>();
        services.AddSingleton(grants);

        // Registered after the composition's own workers were removed, so it is the one hosted service of MailFathom's
        // that starts: the users have to be held before the first request, and the container exists only once the host
        // is built.
        services.AddHostedService(provider => new HoldsUsersOnStart(provider.GetRequiredService<ServedUsers>(), users));
    }

    private sealed class HoldsUsersOnStart(ServedUsers served, IReadOnlyCollection<UserId> users) : IHostedService
    {
        /// <summary>The version a record is first committed at, which is the least a held composition may state.</summary>
        private const long FirstVersion = 1;

        public Task StartAsync(CancellationToken cancellationToken)
        {
            foreach (var user in users.Distinct())
            {
                served.Published(new ServedUser(user, $"user-{user.Value:N}", []), FirstVersion);
            }

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
