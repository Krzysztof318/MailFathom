// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Mail.Export;
using MailFathom.Domain.Exports;

namespace MailFathom.Application.UnitTests.TestDoubles;

/// <summary>Keeps an archive in memory, and makes it readable only once the write is completed.</summary>
/// <remarks>
/// The invisibility before completion is what a test about a failed export is asserting, so it is reproduced here
/// rather than the transport: a write that was abandoned leaves the store holding nothing under its key, exactly as an
/// aborted multipart upload does.
/// </remarks>
internal sealed class InMemoryMailboxExportArchiveStore(bool isAvailable = true) : IMailboxExportArchiveStore
{
    private readonly Dictionary<string, byte[]> completed = [];

    /// <inheritdoc />
    public bool IsAvailable { get; } = isAvailable;

    /// <summary>Gets the keys the store has been asked to delete, in the order it was asked.</summary>
    internal List<string> Deleted { get; } = [];

    /// <summary>Gets how many writes were opened and never completed.</summary>
    internal int AbandonedWrites { get; private set; }

    /// <summary>Reads back one finished archive's bytes.</summary>
    /// <param name="objectLocator">The key the export recorded.</param>
    /// <returns>The bytes, or <see langword="null" /> when the store holds nothing under that key.</returns>
    internal byte[]? Read(string objectLocator) =>
        this.completed.TryGetValue(objectLocator, out var held) ? held : null;

    /// <inheritdoc />
    public Task<MailboxExportArchiveWrite> BeginWriteAsync(
        MailboxExportId exportId,
        CancellationToken cancellationToken) =>
        Task.FromResult<MailboxExportArchiveWrite>(
            new InMemoryWrite(this, $"mailbox-exports/{exportId.Value}"));

    /// <inheritdoc />
    public Task<Stream?> OpenReadAsync(string objectLocator, CancellationToken cancellationToken) =>
        Task.FromResult<Stream?>(this.completed.TryGetValue(objectLocator, out var held)
            ? new MemoryStream(held, writable: false)
            : null);

    /// <inheritdoc />
    public Task DeleteAsync(string objectLocator, CancellationToken cancellationToken)
    {
        this.Deleted.Add(objectLocator);
        this.completed.Remove(objectLocator);

        return Task.CompletedTask;
    }

    /// <summary>One archive being produced, which nothing can read until it is completed.</summary>
    private sealed class InMemoryWrite(InMemoryMailboxExportArchiveStore store, string objectLocator)
        : MailboxExportArchiveWrite
    {
        private readonly MemoryStream buffer = new();

        private bool finished;

        public override string ObjectLocator { get; } = objectLocator;

        public override Stream Content => this.buffer;

        public override Task<long> CompleteAsync(CancellationToken cancellationToken)
        {
            store.completed[this.ObjectLocator] = this.buffer.ToArray();
            this.finished = true;

            return Task.FromResult(this.buffer.Length);
        }

        protected override ValueTask DisposeAsyncCore()
        {
            if (!this.finished)
            {
                store.AbandonedWrites++;
            }

            this.buffer.Dispose();

            return ValueTask.CompletedTask;
        }
    }
}
