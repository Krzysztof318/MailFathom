// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Accounts;

namespace MailFathom.TestSupport;

/// <summary>Builds the caller-scoped catalog a use case reads, over the accounts a deployment serves.</summary>
/// <remarks>
/// It composes the real <see cref="AssignedMailAccountCatalog" /> rather than substituting the port, because a test about
/// the user axis that stubbed the answer would be asserting its own arrangement: what makes an account somebody else's
/// is the intersection that type takes between the accounts assigned to the user a caller was admitted for and the
/// accounts the deployment serves. A test that needs accounts served and says nothing about the user substitutes the
/// port instead.
/// </remarks>
internal static class AssignedMailAccountCatalogs
{
    /// <summary>Builds the catalog over accounts the deployment's own user is assigned, and nobody else.</summary>
    /// <param name="authorization">The authorization the use case under test is reached with, which names the user acted for.</param>
    /// <param name="servedAccounts">The accounts this deployment serves, all of them assigned to <see cref="SyntheticMailUser.Deployment" />.</param>
    /// <returns>The catalog, answering with every account named when the caller is that user and with none when they are not.</returns>
    /// <remarks>
    /// The assignment names <see cref="SyntheticMailUser.Deployment" /> rather than whoever the authorization acts for,
    /// which is what keeps the caller axis a thing a test can vary: a helper assigning every mailbox to whoever asked
    /// would answer the same for every caller, and every test claiming one user cannot reach another's mailbox would
    /// pass while proving nothing. A test naming two users, or assigning one mailbox to both, states the relation
    /// itself through the overload below.
    /// </remarks>
    internal static ICallerMailAccountCatalog For(
        AccessAuthorization authorization,
        params ServedMailAccount[] servedAccounts)
    {
        ArgumentNullException.ThrowIfNull(servedAccounts);

        return For(
            authorization,
            new StubMailAccountAssignments().Assigning(
                SyntheticMailUser.Deployment,
                [.. servedAccounts.Select(account => account.Id)]),
            servedAccounts);
    }

    /// <summary>Builds the catalog over stated assignments, which is what a test about who reaches what arranges.</summary>
    /// <param name="authorization">The authorization the use case under test is reached with, which names the user acted for.</param>
    /// <param name="assignments">Which users are assigned which of the accounts served.</param>
    /// <param name="servedAccounts">The accounts this deployment serves.</param>
    /// <returns>The catalog, answering with the served accounts the caller's user is assigned and with none of anybody else's.</returns>
    /// <remarks>
    /// The accounts are ordered the way the deployment's own catalog orders them, because a scope resolved from this set
    /// is canonical and a test that arranged them any other way would be proving something the process never sees.
    /// </remarks>
    internal static ICallerMailAccountCatalog For(
        AccessAuthorization authorization,
        IMailAccountAssignments assignments,
        params ServedMailAccount[] servedAccounts)
    {
        ArgumentNullException.ThrowIfNull(servedAccounts);

        return new AssignedMailAccountCatalog(
            new DeploymentServing([.. servedAccounts.OrderBy(account => account.Id.Value, StringComparer.Ordinal)]),
            assignments,
            authorization);
    }

    /// <summary>The deployment's own catalog, answering with the accounts a test named.</summary>
    private sealed class DeploymentServing(IReadOnlyList<ServedMailAccount> servedAccounts)
        : IDeploymentMailAccountCatalog
    {
        public bool SynchronizationEnabled => true;

        public IReadOnlyList<ServedMailAccount> ServedAccounts { get; } = servedAccounts;
    }
}
