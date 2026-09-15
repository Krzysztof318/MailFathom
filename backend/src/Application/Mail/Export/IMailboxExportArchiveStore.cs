// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Exports;

namespace MailFathom.Application.Mail.Export;

/// <summary>Writes an export's archive into the content store as it is produced, serves it back, and removes it.</summary>
/// <remarks>
/// <para>
/// A port of its own beside <c>IEmailContentStore</c>, and the one content write in MailFathom that is not byte-based.
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0017-object-storage-content-backend-consistency-and-object-identity.md">ADR 0017</see> § 4
/// keeps every mail payload whole in memory because a message arrives whole and its digest is computed over all of it;
/// an archive is neither — it is produced a message at a time and is as large as a mailbox, which is past what a
/// <c>bytea</c> column holds and far past what a process should. So the archive alone is written as a stream, and the
/// rest of the content port is unchanged.
/// </para>
/// <para>
/// Unlike the mail payload port, this one is registered whatever the deployment selected, because the job handler and
/// the expiry sweep are registered whatever it selected too. <see cref="IsAvailable" /> is therefore a fact a use case
/// reads rather than a failure it meets: a deployment writing content to its database refuses the export before a job
/// exists, naming the setting that would turn it on.
/// </para>
/// <para>
/// Nothing here logs a key or any part of an archive. A key names one person's whole mailbox, which makes it the most
/// concentrated identifier this system mints.
/// </para>
/// </remarks>
public interface IMailboxExportArchiveStore
{
    /// <summary>Gets whether this deployment has somewhere to keep an archive at all.</summary>
    bool IsAvailable { get; }

    /// <summary>Opens a write the archive is produced into, under a key minted for it.</summary>
    /// <param name="exportId">The export the archive belongs to, which reaches the key as a segment.</param>
    /// <param name="cancellationToken">Cancels opening the write.</param>
    /// <returns>The write, carrying the key and the stream the archive is produced into.</returns>
    /// <remarks>
    /// Nothing is readable under the key until the write is completed, which is what keeps a failed export from leaving
    /// a partial archive anybody could download. A write abandoned without completing leaves nothing behind.
    /// </remarks>
    Task<MailboxExportArchiveWrite> BeginWriteAsync(MailboxExportId exportId, CancellationToken cancellationToken);

    /// <summary>Opens the finished archive for reading, so any replica can serve it.</summary>
    /// <param name="objectLocator">The whole key, exactly as the export's record carries it.</param>
    /// <param name="cancellationToken">Cancels opening the read.</param>
    /// <returns>The stream, or <see langword="null" /> when the store holds no object under that key.</returns>
    /// <remarks>An absent object is answered rather than raised, because the record saying an archive exists and the store not holding it is a finding the caller grades rather than a transport failure.</remarks>
    Task<Stream?> OpenReadAsync(string objectLocator, CancellationToken cancellationToken);

    /// <summary>Removes one archive, which is what an expiry, a deletion, and a cancellation each carry through to the store.</summary>
    /// <param name="objectLocator">The whole key, exactly as the export's record carried it.</param>
    /// <param name="cancellationToken">Cancels the removal.</param>
    /// <returns>A task that completes once the store holds no object under that key.</returns>
    /// <remarks>Removing a key nothing holds succeeds, which is what makes the operation safe to repeat after a crash.</remarks>
    Task DeleteAsync(string objectLocator, CancellationToken cancellationToken);
}

/// <summary>One archive being produced into the content store, which is complete only when it is completed.</summary>
/// <remarks>
/// Disposal alone abandons the write, so a job that stopped for any reason leaves nothing downloadable behind — the
/// failure an export must never have is a half-written mailbox that looks like a whole one.
/// </remarks>
public abstract class MailboxExportArchiveWrite : IAsyncDisposable
{
    /// <summary>Gets the whole key the archive is being written under, which the export's record keeps.</summary>
    public abstract string ObjectLocator { get; }

    /// <summary>Gets the stream the archive is produced into.</summary>
    public abstract Stream Content { get; }

    /// <summary>Finishes the archive, after which the key names a complete object.</summary>
    /// <param name="cancellationToken">Cancels the completion.</param>
    /// <returns>How many bytes the finished archive holds.</returns>
    public abstract Task<long> CompleteAsync(CancellationToken cancellationToken);

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await this.DisposeAsyncCore();
        GC.SuppressFinalize(this);
    }

    /// <summary>Abandons whatever has not been completed, leaving the store holding nothing under the key.</summary>
    /// <returns>A task that completes once the store is left clean.</returns>
    protected abstract ValueTask DisposeAsyncCore();
}
