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
/// corresponds with. Both resolve to the sole user this deployment serves where it serves exactly one, so the book an
/// operator manages and the book collection writes into are that person's book rather than an unscoped one. Where the
/// roster holds several, there is no sole user for an act carrying none to be attributed to and
/// <see cref="IDeploymentMailUserSource" /> refuses rather than choosing one — which is what keeps this a
/// resolution rather than an assumption now that a deployment holds as many user records as it was given and a
/// mailbox is assigned to however many of them an administrator assigned it to.
/// </para>
/// <para>
/// It is deliberately not the empty answer <see cref="Accounts.AssignedMailAccountCatalog" /> gives a caller acting for another
/// user. There the question is which of the accounts this deployment serves belong to the caller, and nobody's is a
/// meaningful answer; here the question is which book to read, and a book belonging to nobody is not one. What a caller
/// acting for another user gets is that user's own book — empty until they write in it — rather than this one's.
/// </para>
/// <para>
/// What is left for a deployment serving several is a contact book of its own for work that acts for nobody, or an
/// administrative surface that names the user it is acting for. Neither is decided here, and until one of them is
/// this is the place that states the limit rather than the place that hides it.
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
