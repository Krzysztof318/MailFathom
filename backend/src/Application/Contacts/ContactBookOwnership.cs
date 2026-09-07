// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Domain.Access;

namespace MailFathom.Application.Contacts;

/// <summary>Answers whose contact book the work in hand reads and writes.</summary>
/// <remarks>
/// <para>
/// The user axis enters the contact book here and nowhere else, which is what keeps a per-user book from being a
/// predicate every read and every write has to remember to carry. Each act resolves the user once and hands it to the
/// store and the directory, and both take it as an argument rather than discovering it, so a read that forgot it does
/// not compile.
/// </para>
/// <para>
/// A caller admitted on a surface that serves one person their own mail is acting for that user, and their book is
/// that user's. Two principals carry no user and reach the book all the same: the deployment administrator, whose
/// acts are the deployment's, and this process's own identity, under which collection records the people an account
/// corresponds with. Both resolve to the user this deployment serves, because while mail accounts, users, and
/// credentials are declared in configuration a deployment holds exactly one user record and every account it
/// synchronizes is that user's — so the book an operator manages and the book collection writes into are that one
/// person's book rather than an unscoped one. The invariant is established before the host serves anything, which is
/// what makes this a resolution rather than an assumption.
/// </para>
/// <para>
/// It is deliberately not the empty answer <see cref="Accounts.OwnedMailAccountCatalog" /> gives a caller acting for another
/// user. There the question is which of the accounts this deployment serves belong to the caller, and nobody's is a
/// meaningful answer; here the question is which book to read, and a book belonging to nobody is not one. What a caller
/// acting for another user gets is that user's own book — empty until they write in it — rather than this one's.
/// </para>
/// <para>
/// When accounts, users, and credentials move into the database together, the administrative surface names the user
/// it is acting for and collection reads the account's own. This is the one place that changes.
/// </para>
/// </remarks>
public sealed class ContactBookOwnership
{
    private readonly AccessAuthorization authorization;
    private readonly IDeploymentMailUserSource deploymentUser;

    /// <summary>Initializes the resolution over the principal the work was admitted under.</summary>
    /// <param name="authorization">Answers which user the work in hand is acting for, where it acts for one.</param>
    /// <param name="deploymentUser">Names the user whose book a principal acting for none reaches.</param>
    /// <exception cref="ArgumentNullException">Thrown when a required collaborator is <see langword="null" />.</exception>
    public ContactBookOwnership(AccessAuthorization authorization, IDeploymentMailUserSource deploymentUser)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentNullException.ThrowIfNull(deploymentUser);

        this.authorization = authorization;
        this.deploymentUser = deploymentUser;
    }

    /// <summary>Gets the user whose contact book this unit of work reads and writes.</summary>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the work was reached under no principal at all.</exception>
    public MailUserId User => this.authorization.ActingUser ?? this.deploymentUser.User;
}
