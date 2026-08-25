// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Accounts;
using MailFathom.Domain.Access;

namespace MailFathom.TestSupport;

/// <summary>Builds the caller-scoped catalog a use case reads, over the accounts a deployment serves.</summary>
/// <remarks>
/// It composes the real <see cref="OwnedMailAccountCatalog" /> rather than substituting the port, because a test about
/// the owner axis that stubbed the answer would be asserting its own arrangement: what makes an account somebody else's
/// is the comparison that type performs between the owner a caller was admitted for and the owner every configured
/// account belongs to. A test that needs accounts served and says nothing about the owner substitutes the port instead.
/// </remarks>
internal static class OwnedMailAccountCatalogs
{
    /// <summary>Builds the catalog a caller admitted under one authorization reads.</summary>
    /// <param name="authorization">The authorization the use case under test is reached with, which names the owner acted for.</param>
    /// <param name="servedAccounts">The accounts this deployment serves, every one of which belongs to its own owner.</param>
    /// <returns>The catalog, answering with every served account for the deployment's owner and with none for anybody else.</returns>
    /// <remarks>
    /// The accounts are ordered the way the deployment's own catalog orders them, because a scope resolved from this set
    /// is canonical and a test that arranged them any other way would be proving something the process never sees.
    /// </remarks>
    internal static ICallerMailAccountCatalog For(
        AccessAuthorization authorization,
        params ServedMailAccount[] servedAccounts) =>
        new OwnedMailAccountCatalog(
            new DeploymentServing([.. servedAccounts.OrderBy(account => account.Id.Value, StringComparer.Ordinal)]),
            new OwnedByTheDeployment(),
            authorization);

    /// <summary>The deployment's own catalog, answering with the accounts a test named.</summary>
    private sealed class DeploymentServing(IReadOnlyList<ServedMailAccount> servedAccounts)
        : IDeploymentMailAccountCatalog
    {
        public bool SynchronizationEnabled => true;

        public IReadOnlyList<ServedMailAccount> ServedAccounts { get; } = servedAccounts;
    }

    /// <summary>The one owner every configured account belongs to while accounts are declared in a file.</summary>
    private sealed class OwnedByTheDeployment : IDeploymentMailOwnerSource
    {
        public MailOwnerId Owner => SyntheticMailOwner.Deployment;
    }
}
