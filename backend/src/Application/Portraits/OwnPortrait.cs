// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Domain.Access;

namespace MailFathom.Application.Portraits;

/// <summary>Reads, replaces, and removes the picture the signed-in person is drawn by.</summary>
/// <remarks>
/// <para>
/// Whose portrait this is comes from the principal rather than from the request, exactly as it does for the owner
/// record and for a person's preferences: there is no argument here for another owner's identifier, so a request about
/// somebody else is something a caller cannot express rather than something a surface has to refuse.
/// </para>
/// <para>
/// All three acts are admitted under <see cref="MailFathomPermission.MailRead" />, the grant a signed-in person
/// already holds, and none of them adds a name to the published set. The two writes are deliberately not
/// <see cref="MailFathomPermission.MailAccountsWrite" />: that grant decides which mailboxes this deployment connects
/// to, an administrator maintains it for somebody whose accounts are declared in the deployment's own files, and what
/// a person is drawn by must not be decided by a grant over their mail configuration.
/// </para>
/// <para>
/// A read answers nothing where there is no portrait, rather than refusing: a client draws the initials it already has
/// from the person's name, so an absent picture is an ordinary state of the screen rather than an error on it. Octets
/// that are no image kind this build stores are answered the same way, because a row nothing here could have written
/// is a row this surface has nothing to say about.
/// </para>
/// </remarks>
public sealed class OwnPortrait
{
    private readonly AccessAuthorization authorization;
    private readonly IOwnerPortraitStore store;

    /// <summary>Initializes the use case.</summary>
    /// <param name="authorization">Reports the grant the caller holds and the owner it acts for.</param>
    /// <param name="store">Holds one person's portrait.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public OwnPortrait(AccessAuthorization authorization, IOwnerPortraitStore store)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentNullException.ThrowIfNull(store);

        this.authorization = authorization;
        this.store = store;
    }

    /// <summary>Reads the picture the signed-in person is drawn by.</summary>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>Their portrait, or <see langword="null" /> where this deployment holds none for them.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller acts for no owner, or its grant omits <see cref="MailFathomPermission.MailRead" />.</exception>
    public async Task<OwnerPortrait?> ReadAsync(CancellationToken cancellationToken)
    {
        this.authorization.RequirePermission(MailFathomPermission.MailRead);

        var stored = await this.store.ReadAsync(this.authorization.RequireOwner(), cancellationToken);

        return stored is { } content ? OwnerPortrait.Of(content) : null;
    }

    /// <summary>Replaces the picture the signed-in person is drawn by.</summary>
    /// <param name="portrait">The portrait, whose octets are stored as they were supplied.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns><see langword="true" /> when the write landed, and <see langword="false" /> when this deployment holds no record for the caller.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="portrait" /> is <see langword="null" />.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller acts for no owner, or its grant omits <see cref="MailFathomPermission.MailRead" />.</exception>
    public Task<bool> ReplaceAsync(OwnerPortrait portrait, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(portrait);

        this.authorization.RequirePermission(MailFathomPermission.MailRead);

        return this.store.SaveAsync(this.authorization.RequireOwner(), portrait, cancellationToken);
    }

    /// <summary>Removes the picture the signed-in person is drawn by.</summary>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller acts for no owner, or its grant omits <see cref="MailFathomPermission.MailRead" />.</exception>
    /// <remarks>Nothing else about the person is touched, and the removal is silent about whether there was one to remove.</remarks>
    public Task RemoveAsync(CancellationToken cancellationToken)
    {
        this.authorization.RequirePermission(MailFathomPermission.MailRead);

        return this.store.RemoveAsync(this.authorization.RequireOwner(), cancellationToken);
    }
}
