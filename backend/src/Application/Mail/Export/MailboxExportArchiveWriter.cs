// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.IO.Compression;
using System.Text;
using System.Text.Json;
using MailFathom.Domain.Exports;

namespace MailFathom.Application.Mail.Export;

/// <summary>Produces the zip archive one export carries: a Maildir per folder, one file per message, and the keywords beside them.</summary>
/// <remarks>
/// <para>
/// A zip rather than the tar stream
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0034-holding-a-mailbox-mailfathom-alone-keeps.md">ADR 0034</see>
/// first decided, because the reader is a person leaving the product and every desktop operating system opens a zip
/// with no tooling at all. What is inside it is unchanged: the Maildir++ layout, the stored bytes exactly, the standard
/// flags in each file name, the encoded folder names, and the keywords in a document of their own at the root.
/// </para>
/// <para>
/// It holds one message at a time and never a folder's contents, which is what lets a mailbox of any size be written
/// through it. The one thing it accumulates is the keywords of the messages that carry any — most carry none — because
/// the document naming them is one entry and an entry is written whole.
/// </para>
/// <para>
/// Every entry it writes is a relative path under the archive root, composed by <see cref="MaildirArchiveLayout" /> from
/// encoded segments. None is absolute and none is a link, so no folder name a person or a server chose can place a file
/// outside the archive when it is extracted.
/// </para>
/// </remarks>
public sealed class MailboxExportArchiveWriter : IAsyncDisposable
{
    /// <summary>The three subdirectories a Maildir has, whether or not the folder holds anything.</summary>
    private static readonly string[] MaildirSubdirectories = ["cur", "new", "tmp"];

    /// <summary>
    /// How hard the archive is compressed. Mail is mostly text and compresses well at the cheapest level, while an
    /// attachment is usually already compressed and gains nothing at any level — so the slower levels would spend an
    /// exporting deployment's processor on bytes that do not shrink.
    /// </summary>
    private const CompressionLevel Compression = CompressionLevel.Fastest;

    private readonly ZipArchive archive;
    private readonly List<KeywordedMessage> keyworded = [];

    /// <summary>Opens an archive over the stream it is produced into.</summary>
    /// <param name="destination">The stream the archive is written to, which need not be seekable.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="destination" /> is <see langword="null" />.</exception>
    /// <remarks>The stream is left open, because what closes it is the write to the content store that owns it.</remarks>
    public MailboxExportArchiveWriter(Stream destination)
    {
        ArgumentNullException.ThrowIfNull(destination);

        // UTF-8 entry names, which is what every modern extractor reads. The names are percent-encoded ASCII anyway,
        // so the encoding decides nothing about them; stating it keeps the archive's own flag honest.
        this.archive = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: true, Encoding.UTF8);
    }

    /// <summary>Writes the empty Maildir one folder has, so a folder holding nothing is still in the archive.</summary>
    /// <param name="folder">The folder.</param>
    /// <returns>The directory the folder's messages are written under.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="folder" /> is <see langword="null" />.</exception>
    public string OpenFolder(MailboxExportFolder folder)
    {
        ArgumentNullException.ThrowIfNull(folder);

        var directory = MaildirArchiveLayout.FolderDirectory(folder.Segments, folder.IsInbox);

        foreach (var subdirectory in MaildirSubdirectories)
        {
            var name = directory.Length == 0 ? $"{subdirectory}/" : $"{directory}/{subdirectory}/";
            this.archive.CreateEntry(name, Compression);
        }

        return directory;
    }

    /// <summary>Writes one message into its folder's Maildir, exactly as it is stored.</summary>
    /// <param name="folderDirectory">What <see cref="OpenFolder" /> answered for the folder the message is in.</param>
    /// <param name="message">The message, which supplies its arrival, its length, its flags, and its keywords.</param>
    /// <param name="rawMime">The stored bytes, written through unchanged.</param>
    /// <param name="ordinal">The message's position in this export, which makes its file name unique.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The path the message was written at.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="message" /> is <see langword="null" />.</exception>
    public async Task<string> WriteMessageAsync(
        string folderDirectory,
        ExportableMessage message,
        ReadOnlyMemory<byte> rawMime,
        long ordinal,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(folderDirectory);
        ArgumentNullException.ThrowIfNull(message);

        var fileName = MaildirArchiveLayout.MessageFileName(
            message.ReceivedAt,
            ordinal,
            rawMime.Length,
            message.Flags);

        var path = MaildirArchiveLayout.MessagePath(folderDirectory, fileName);

        var entry = this.archive.CreateEntry(path, Compression);
        entry.LastWriteTime = message.ReceivedAt;

        await using (var content = entry.Open())
        {
            await content.WriteAsync(rawMime, cancellationToken);
        }

        if (message.Keywords.Count > 0)
        {
            this.keyworded.Add(new KeywordedMessage(path, message.Keywords));
        }

        return path;
    }

    /// <summary>Writes the document naming the keywords Maildir file names have no form for.</summary>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes once the document is in the archive.</returns>
    /// <remarks>
    /// Written last because it names the paths the messages were written at, and written whatever the mailbox carries —
    /// an empty document says the export found no keyword, which is a different statement from an archive that forgot
    /// to make one.
    /// </remarks>
    public async Task WriteKeywordsAsync(CancellationToken cancellationToken)
    {
        var entry = this.archive.CreateEntry(MaildirArchiveLayout.KeywordsDocumentPath, Compression);

        await using var content = entry.Open();
        await using var writer = new Utf8JsonWriter(content, new JsonWriterOptions { Indented = true });

        writer.WriteStartObject();
        writer.WriteStartArray("messages");

        foreach (var message in this.keyworded)
        {
            writer.WriteStartObject();
            writer.WriteString("path", message.Path);
            writer.WriteStartArray("keywords");

            foreach (var keyword in message.Keywords)
            {
                writer.WriteStringValue(keyword);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();

        await writer.FlushAsync(cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        // Disposing the archive writes its central directory, which is what makes the stream a readable zip. It is
        // synchronous in the framework, and the stream beneath it is the content store's own, which the caller closes.
        this.archive.Dispose();

        return ValueTask.CompletedTask;
    }

    private sealed record KeywordedMessage(string Path, IReadOnlyList<string> Keywords);
}
