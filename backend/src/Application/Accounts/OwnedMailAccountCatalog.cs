// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;

namespace MailFathom.Application.Accounts;

/// <summary>Answers which of the accounts this deployment serves belong to the owner the work in hand is acting for.</summary>
/// <remarks>
/// <para>
/// The owner axis enters the mailbox here and nowhere else. Every caller-facing resolution reads this, so narrowing a
/// read to one owner is one decision taken once rather than a predicate each read model has to remember to carry, and a
/// read model that reached the deployment's catalog instead names a member this port does not publish.
/// </para>
/// <para>
/// A configured mail account names no owner, so what decides the answer is the deployment's own single-owner invariant
/// rather than a per-account column: while accounts are declared in configuration a deployment holds exactly one owner
/// record and every configured account belongs to it. A caller acting for that owner therefore owns everything the
/// deployment serves, and a caller acting for any other owner owns none of it. The invariant is established before the
/// host serves anything, so this is a comparison rather than a hope. When accounts, owners, and credentials move into
/// the database together, what an owner owns becomes a column and this reads that instead; nothing above it changes,
/// which is the point of resolving it here.
/// </para>
/// <para>
/// The empty answer and the refusal are deliberately different outcomes. An owner who owns nothing is answered with an
/// empty set, which the resolution turns into a scope that reads nothing; a principal acting for no owner is refused,
/// because an empty answer there would let this process's own identity or the deployment administrator reach a
/// caller-facing read and be told, in the shape of an answer, that they own nothing.
/// </para>
/// </remarks>
public sealed class OwnedMailAccountCatalog : ICallerMailAccountCatalog
{
    private readonly IDeploymentMailAccountCatalog servedAccounts;
    private readonly IDeploymentMailOwnerSource deploymentOwner;
    private readonly AccessAuthorization authorization;

    /// <summary>Initializes the caller-scoped catalog.</summary>
    /// <param name="servedAccounts">Describes every account this deployment serves.</param>
    /// <param name="deploymentOwner">Names the owner every configured account belongs to.</param>
    /// <param name="authorization">Answers which owner the work in hand is acting for.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public OwnedMailAccountCatalog(
        IDeploymentMailAccountCatalog servedAccounts,
        IDeploymentMailOwnerSource deploymentOwner,
        AccessAuthorization authorization)
    {
        ArgumentNullException.ThrowIfNull(servedAccounts);
        ArgumentNullException.ThrowIfNull(deploymentOwner);
        ArgumentNullException.ThrowIfNull(authorization);

        this.servedAccounts = servedAccounts;
        this.deploymentOwner = deploymentOwner;
        this.authorization = authorization;
    }

    /// <inheritdoc />
    public bool SynchronizationEnabled => this.servedAccounts.SynchronizationEnabled;

    /// <inheritdoc />
    public IReadOnlyList<ServedMailAccount> OwnedAccounts =>
        this.authorization.RequireOwner() == this.deploymentOwner.Owner
            ? this.servedAccounts.ServedAccounts
            : [];
}
