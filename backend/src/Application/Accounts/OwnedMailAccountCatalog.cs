// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Domain.Access;

namespace MailFathom.Application.Accounts;

/// <summary>Answers which of the accounts this deployment serves belong to the owner the work in hand is acting for.</summary>
/// <remarks>
/// <para>
/// The owner axis enters the mailbox here and nowhere else. Every caller-facing resolution reads this, so narrowing a
/// read to one owner is one decision taken once rather than a predicate each read model has to remember to carry, and a
/// read model that reached the deployment's catalog instead names a member this port does not publish.
/// </para>
/// <para>
/// What decides the answer is the owner each served account already carries. The deployment's own
/// <c>MailSynchronization:Accounts</c> section names nobody, so the roster is what attributes its accounts, and an
/// owner's own declared section or record arrives with the owner attached; either way the attribution is settled before
/// this reads it, and a caller owns exactly the accounts attributed to the owner they were admitted for. Nothing here
/// compares against a sole owner the deployment holds, because a deployment whose owner-facing surfaces authenticate
/// every caller as a person serves several — which is the arrangement the roster exists for, and one where asking for a
/// sole owner has no answer to give.
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
    private readonly AccessAuthorization authorization;

    /// <summary>Initializes the caller-scoped catalog.</summary>
    /// <param name="servedAccounts">Describes every account this deployment serves, each under the owner it belongs to.</param>
    /// <param name="authorization">Answers which owner the work in hand is acting for.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public OwnedMailAccountCatalog(
        IDeploymentMailAccountCatalog servedAccounts,
        AccessAuthorization authorization)
    {
        ArgumentNullException.ThrowIfNull(servedAccounts);
        ArgumentNullException.ThrowIfNull(authorization);

        this.servedAccounts = servedAccounts;
        this.authorization = authorization;
    }

    /// <inheritdoc />
    public bool SynchronizationEnabled => this.servedAccounts.SynchronizationEnabled;

    /// <inheritdoc />
    public MailOwnerId Owner => this.authorization.RequireOwner();

    /// <inheritdoc />
    /// <remarks>
    /// The order the deployment's catalog established is preserved, because a scope resolved from this set is what a
    /// continuation cursor is issued against and filtering a canonical order leaves it canonical.
    /// </remarks>
    public IReadOnlyList<ServedMailAccount> OwnedAccounts
    {
        get
        {
            var owner = this.authorization.RequireOwner();

            return [.. this.servedAccounts.ServedAccounts.Where(account => account.Owner == owner)];
        }
    }
}
