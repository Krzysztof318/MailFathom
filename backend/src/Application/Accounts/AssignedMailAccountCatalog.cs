// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Domain.Access;

namespace MailFathom.Application.Accounts;

/// <summary>Answers which of the accounts this deployment serves are assigned to the user the work in hand is acting for.</summary>
/// <remarks>
/// <para>
/// The user axis enters the mailbox here and nowhere else. Every caller-facing resolution reads this, so narrowing a
/// read to one person is one decision taken once rather than a predicate each read model has to remember to carry, and
/// a read model that reached the deployment's catalog instead names a member this port does not publish.
/// </para>
/// <para>
/// What decides the answer is the assignment relation rather than a user carried on the account. A mailbox is one
/// mailbox however many people read it, so the account arrives with no user attached and the assignments say who
/// reaches it; a caller is served exactly the accounts assigned to the user they were admitted for, and two callers
/// assigned one account are served the same mailbox rather than a copy each.
/// </para>
/// <para>
/// The empty answer and the refusal are deliberately different outcomes. A user assigned nothing is answered with an
/// empty set, which the resolution turns into a scope that reads nothing; a principal acting for no user is refused,
/// because an empty answer there would let this process's own identity or the deployment administrator reach a
/// caller-facing read and be told, in the shape of an answer, that they are assigned nothing.
/// </para>
/// </remarks>
public sealed class AssignedMailAccountCatalog : ICallerMailAccountCatalog
{
    private readonly IDeploymentMailAccountCatalog servedAccounts;
    private readonly IMailAccountAssignments assignments;
    private readonly AccessAuthorization authorization;

    /// <summary>Initializes the caller-scoped catalog.</summary>
    /// <param name="servedAccounts">Describes every account this deployment serves.</param>
    /// <param name="assignments">Answers which accounts a user is assigned.</param>
    /// <param name="authorization">Answers which user the work in hand is acting for.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public AssignedMailAccountCatalog(
        IDeploymentMailAccountCatalog servedAccounts,
        IMailAccountAssignments assignments,
        AccessAuthorization authorization)
    {
        ArgumentNullException.ThrowIfNull(servedAccounts);
        ArgumentNullException.ThrowIfNull(assignments);
        ArgumentNullException.ThrowIfNull(authorization);

        this.servedAccounts = servedAccounts;
        this.assignments = assignments;
        this.authorization = authorization;
    }

    /// <inheritdoc />
    public bool SynchronizationEnabled => this.servedAccounts.SynchronizationEnabled;

    /// <inheritdoc />
    public UserId User => this.authorization.RequireUser();

    /// <inheritdoc />
    /// <remarks>
    /// The intersection is taken against the deployment's own set rather than against the assignments alone, so an
    /// assignment naming an account the deployment no longer serves contributes nothing — which is the same answer
    /// every other reader gives about such an account. The order the deployment's catalog established is preserved,
    /// because a scope resolved from this set is what a continuation cursor is issued against and filtering a
    /// canonical order leaves it canonical.
    /// </remarks>
    public IReadOnlyList<ServedMailAccount> AssignedAccounts
    {
        get
        {
            var user = this.authorization.RequireUser();
            var assigned = this.assignments.AccountsAssignedTo(user).ToHashSet();

            return [.. this.servedAccounts.ServedAccounts.Where(account => assigned.Contains(account.Id))];
        }
    }
}
