// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Accounts;

namespace MailFathom.TestSupport;

/// <summary>Builds the caller-scoped catalog a use case reads, over the accounts a deployment serves.</summary>
/// <remarks>
/// It composes the real <see cref="OwnedMailAccountCatalog" /> rather than substituting the port, because a test about
/// the owner axis that stubbed the answer would be asserting its own arrangement: what makes an account somebody else's
/// is the comparison that type performs between the owner a caller was admitted for and the owner each served account
/// carries. A test that needs accounts served and says nothing about the owner substitutes the port instead.
/// </remarks>
internal static class OwnedMailAccountCatalogs
{
    /// <summary>Builds the catalog a caller admitted under one authorization reads.</summary>
    /// <param name="authorization">The authorization the use case under test is reached with, which names the owner acted for.</param>
    /// <param name="servedAccounts">The accounts this deployment serves, each carrying the owner it belongs to.</param>
    /// <returns>The catalog, answering each owner with the served accounts attributed to them and with none of anybody else's.</returns>
    /// <remarks>
    /// The accounts are ordered the way the deployment's own catalog orders them, because a scope resolved from this set
    /// is canonical and a test that arranged them any other way would be proving something the process never sees.
    /// </remarks>
    internal static ICallerMailAccountCatalog For(
        AccessAuthorization authorization,
        params ServedMailAccount[] servedAccounts) =>
        new OwnedMailAccountCatalog(
            new DeploymentServing([.. servedAccounts.OrderBy(account => account.Id.Value, StringComparer.Ordinal)]),
            authorization);

    /// <summary>The deployment's own catalog, answering with the accounts a test named.</summary>
    private sealed class DeploymentServing(IReadOnlyList<ServedMailAccount> servedAccounts)
        : IDeploymentMailAccountCatalog
    {
        public bool SynchronizationEnabled => true;

        public IReadOnlyList<ServedMailAccount> ServedAccounts { get; } = servedAccounts;
    }
}
