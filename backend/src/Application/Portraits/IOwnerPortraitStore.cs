// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;

namespace MailFathom.Application.Portraits;

/// <summary>Holds the octets one person is drawn by, and hands them back to whichever machine they next sign in from.</summary>
/// <remarks>
/// <para>
/// One read, one write, and one removal, each for one owner, so there is no shape here that touches two people's
/// portraits and no query that could compose a roster out of them. All three are key lookups for that reason rather
/// than for a plan's.
/// </para>
/// <para>
/// The read hands back octets rather than a portrait, because what kind of image they are is read from them and that
/// reading belongs to the layer that publishes the kinds. A store that returned a kind of its own would be a second
/// opinion about the same octets.
/// </para>
/// <para>
/// The write is last-write-wins and states no version, for the reason a person's preferences do: the only writers are
/// one person's own devices, and there is nobody a lost update could belong to.
/// </para>
/// </remarks>
public interface IOwnerPortraitStore
{
    /// <summary>Reads the octets one person is drawn by.</summary>
    /// <param name="owner">The owner whose portrait is read.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The stored octets, or <see langword="null" /> where this deployment holds no portrait for them.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="owner" /> names nobody.</exception>
    Task<ReadOnlyMemory<byte>?> ReadAsync(MailOwnerId owner, CancellationToken cancellationToken);

    /// <summary>Replaces the portrait one person is drawn by.</summary>
    /// <param name="owner">The owner whose portrait is written.</param>
    /// <param name="portrait">The portrait, whose octets are stored as they were supplied.</param>
    /// <param name="cancellationToken">Cancels the commit.</param>
    /// <returns><see langword="true" /> when the write landed, and <see langword="false" /> when this deployment holds no such owner.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="owner" /> names nobody.</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="portrait" /> is <see langword="null" />.</exception>
    /// <remarks>An owner this deployment does not hold is reported rather than raised, because the caller is a person whose row was erased under a credential that has not yet been withdrawn.</remarks>
    Task<bool> SaveAsync(MailOwnerId owner, OwnerPortrait portrait, CancellationToken cancellationToken);

    /// <summary>Removes the portrait one person is drawn by, leaving everything else about them as it was.</summary>
    /// <param name="owner">The owner whose portrait is removed.</param>
    /// <param name="cancellationToken">Cancels the commit.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="owner" /> names nobody.</exception>
    /// <remarks>Removing what is not there is not a failure and reports nothing: an owner with no portrait and an owner this deployment no longer holds have the same answer, which is that there is now no portrait of theirs.</remarks>
    Task RemoveAsync(MailOwnerId owner, CancellationToken cancellationToken);
}
