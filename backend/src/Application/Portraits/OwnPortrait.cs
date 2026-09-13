// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.StoredFiles;
using MailFathom.Domain.Access;

namespace MailFathom.Application.Portraits;

/// <summary>Reads, replaces, and removes the picture the signed-in person is drawn by.</summary>
/// <remarks>
/// <para>
/// Whose portrait this is comes from the principal rather than from the request, exactly as it does for the user
/// record and for a person's preferences: there is no argument here for another user's identifier, so a request about
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
/// The picture is a stored file the user record links to. A replacement writes the new file, then links it, then
/// removes the file it displaced, so a failure between any two steps leaves at most a file nothing links to, which
/// <see cref="UnlinkedStoredFileSweep" /> removes — never a record linking to a file that is gone.
/// </para>
/// <para>
/// A read answers nothing where there is no portrait, rather than refusing: a client draws the initials it already has
/// from the person's name, so an absent picture is an ordinary state of the screen rather than an error on it. Octets
/// that are no image kind this build stores are answered the same way, because a file nothing here could have written
/// is a file this surface has nothing to say about.
/// </para>
/// </remarks>
public sealed class OwnPortrait
{
    private readonly AccessAuthorization authorization;
    private readonly IStoredFileStore files;
    private readonly IUserRecordFileLinks links;

    /// <summary>Initializes the use case.</summary>
    /// <param name="authorization">Reports the grant the caller holds and the user it acts for.</param>
    /// <param name="files">Holds the file the portrait is.</param>
    /// <param name="links">Reads and rewrites the link from the user record to that file.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public OwnPortrait(AccessAuthorization authorization, IStoredFileStore files, IUserRecordFileLinks links)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(links);

        this.authorization = authorization;
        this.files = files;
        this.links = links;
    }

    /// <summary>Reads the picture the signed-in person is drawn by.</summary>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>Their portrait, or <see langword="null" /> where this deployment holds none for them.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller acts for no user, or its grant omits <see cref="MailFathomPermission.MailRead" />.</exception>
    public async Task<UserPortrait?> ReadAsync(CancellationToken cancellationToken)
    {
        this.authorization.RequirePermission(MailFathomPermission.MailRead);

        var user = this.authorization.RequireUser();

        if (await this.links.FindPortraitAsync(user, cancellationToken) is not { } linked)
        {
            return null;
        }

        return await this.files.ReadAsync(user, linked, cancellationToken) is { } content
            ? UserPortrait.Of(content)
            : null;
    }

    /// <summary>Replaces the picture the signed-in person is drawn by.</summary>
    /// <param name="portrait">The portrait, whose octets are stored as they were supplied.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns><see langword="true" /> when the write landed, and <see langword="false" /> when this deployment holds no record for the caller.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="portrait" /> is <see langword="null" />.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller acts for no user, or its grant omits <see cref="MailFathomPermission.MailRead" />.</exception>
    public async Task<bool> ReplaceAsync(UserPortrait portrait, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(portrait);

        this.authorization.RequirePermission(MailFathomPermission.MailRead);

        var user = this.authorization.RequireUser();

        if (await this.files.WriteAsync(user, portrait.Type.MediaType, portrait.Content, cancellationToken) is not { } written)
        {
            return false;
        }

        var relinked = await this.links.RelinkPortraitAsync(user, written, cancellationToken);

        if (!relinked.UserHeld)
        {
            return false;
        }

        if (relinked.Replaced is { } displaced && displaced != written)
        {
            await this.files.RemoveAsync(user, displaced, cancellationToken);
        }

        return true;
    }

    /// <summary>Removes the picture the signed-in person is drawn by.</summary>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller acts for no user, or its grant omits <see cref="MailFathomPermission.MailRead" />.</exception>
    /// <remarks>
    /// Nothing else about the person is touched, and the removal is silent about whether there was one to remove. The
    /// link goes before the file does, for the reason a replacement orders its steps.
    /// </remarks>
    public async Task RemoveAsync(CancellationToken cancellationToken)
    {
        this.authorization.RequirePermission(MailFathomPermission.MailRead);

        var user = this.authorization.RequireUser();
        var relinked = await this.links.RelinkPortraitAsync(user, null, cancellationToken);

        if (relinked.Replaced is { } displaced)
        {
            await this.files.RemoveAsync(user, displaced, cancellationToken);
        }
    }
}
