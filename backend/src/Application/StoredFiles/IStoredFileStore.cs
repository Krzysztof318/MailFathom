// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;

namespace MailFathom.Application.StoredFiles;

/// <summary>Holds the binary files users supply, wherever this deployment stores content next.</summary>
/// <remarks>
/// <para>
/// A file is a record of its own — who it belongs to, its media type, its length, and its digest — and its octets
/// follow <c>ContentStorage:Backend</c> exactly as mail content does: into the database, or into the object store
/// before the row that points at it is written. A record that shows a file links to it by <see cref="StoredFileId" />
/// and never holds the octets itself.
/// </para>
/// <para>
/// Every act but the sweep's read names the owner beside the file, so an identifier learned elsewhere reaches nobody
/// else's file: a file of another user reads as absent, is not held, and is not removed.
/// </para>
/// <para>
/// Writing a file and linking it are two acts, so a failure between them leaves a file nothing links to. That is the
/// designed failure rather than a leak: <see cref="UnlinkedStoredFileSweep" /> removes such a file once it is older than
/// the time any write takes to be linked.
/// </para>
/// </remarks>
public interface IStoredFileStore
{
    /// <summary>Writes one file for one user, placing its octets before the row that records it.</summary>
    /// <param name="owner">The user the file belongs to.</param>
    /// <param name="mediaType">The media type the file is served under.</param>
    /// <param name="content">The octets, stored as they were supplied.</param>
    /// <param name="cancellationToken">Cancels the placement and the commit.</param>
    /// <returns>The identifier minted for the file, or <see langword="null" /> when this deployment holds no such user.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="owner" /> names nobody, <paramref name="mediaType" /> is blank, or <paramref name="content" /> is empty.</exception>
    Task<StoredFileId?> WriteAsync(
        UserId owner,
        string mediaType,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken);

    /// <summary>Reads the octets of one file of one user.</summary>
    /// <param name="owner">The user the file belongs to.</param>
    /// <param name="file">The file.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The octets, or <see langword="null" /> when this deployment holds no such file for that user.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="owner" /> or <paramref name="file" /> names nothing.</exception>
    Task<ReadOnlyMemory<byte>?> ReadAsync(UserId owner, StoredFileId file, CancellationToken cancellationToken);

    /// <summary>Reports whether this deployment holds one file for one user, without reading its octets.</summary>
    /// <param name="owner">The user the file would belong to.</param>
    /// <param name="file">The file.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns><see langword="true" /> when the file exists and belongs to <paramref name="owner" />.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="owner" /> or <paramref name="file" /> names nothing.</exception>
    Task<bool> HoldsAsync(UserId owner, StoredFileId file, CancellationToken cancellationToken);

    /// <summary>Removes one file of one user, the row with the commit and the object immediately afterwards.</summary>
    /// <param name="owner">The user the file belongs to.</param>
    /// <param name="file">The file.</param>
    /// <param name="cancellationToken">Cancels the removal.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="owner" /> or <paramref name="file" /> names nothing.</exception>
    /// <remarks>Removing what is not there reports nothing, so a removal is safe to repeat.</remarks>
    Task RemoveAsync(UserId owner, StoredFileId file, CancellationToken cancellationToken);

    /// <summary>Finds files written before an instant whose identifier their owner's record does not mention.</summary>
    /// <param name="writtenBefore">The instant a candidate was written before.</param>
    /// <param name="maxFiles">How many candidates to answer at most.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The candidates, oldest first.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="maxFiles" /> is not positive.</exception>
    /// <remarks>
    /// A mention is a coarser test than a link, and deliberately errs one way: a file whose identifier appears anywhere
    /// in the record is left out, so the sweep never spends its bound re-reading files that are linked. Whether a
    /// candidate is actually unlinked is decided by the sweep against the links the record declares.
    /// </remarks>
    Task<IReadOnlyList<HeldStoredFile>> FindUnmentionedAsync(
        DateTimeOffset writtenBefore,
        int maxFiles,
        CancellationToken cancellationToken);
}
