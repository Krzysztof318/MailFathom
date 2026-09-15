// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Domain.Access;

namespace MailFathom.Application.Contacts;

/// <summary>Answers which contact books the caller in hand reads, and which one their writes go into.</summary>
/// <remarks>
/// <para>
/// The user axis enters a caller-facing contact read here and nowhere else, which is what keeps the scope from being a
/// predicate every read has to remember to carry. Each act resolves it once and hands it to the store and the
/// directory, and both take it as an argument rather than discovering it, so a read that forgot it does not compile.
/// </para>
/// <para>
/// A caller is admitted on a surface that serves one person their own mail, so the books they reach are that user's
/// own and the collected book of every mail account assigned to them, which is the same set that decides which mail
/// they read. A principal acting for nobody is refused rather than attributed to somebody: the deployment
/// administrator names the user or the account whose book it reaches, and collection writes into the book of the
/// account it is synchronizing, so neither of them arrives here at all.
/// </para>
/// <para>
/// That refusal is the whole of what this type is for. Resolving a principal that carries no user to "the single user
/// this deployment serves" is what stopped working the day a deployment held several, and it could not be repaired by
/// choosing one — an act attributed to whichever user a read happened to find is how one person is handed another
/// person's correspondents.
/// </para>
/// </remarks>
public sealed class ContactBookOwnership
{
    private readonly AccessAuthorization authorization;
    private readonly ContactBookScopes scopes;

    /// <summary>Initializes the resolution over the principal the work was admitted under.</summary>
    /// <param name="authorization">Answers which user the work in hand is acting for.</param>
    /// <param name="scopes">Composes the books one user reads.</param>
    /// <exception cref="ArgumentNullException">Thrown when a required collaborator is <see langword="null" />.</exception>
    public ContactBookOwnership(AccessAuthorization authorization, ContactBookScopes scopes)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentNullException.ThrowIfNull(scopes);

        this.authorization = authorization;
        this.scopes = scopes;
    }

    /// <summary>Gets the user whose own book this caller's writes go into.</summary>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the work was reached under a principal acting for no user.</exception>
    public MailUserId User => this.authorization.RequireUser();

    /// <summary>Gets the books this caller reads: their own first, then the collected book of each account they are assigned.</summary>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the work was reached under a principal acting for no user.</exception>
    public ContactBookScope Scope => this.scopes.Of(this.User);
}
