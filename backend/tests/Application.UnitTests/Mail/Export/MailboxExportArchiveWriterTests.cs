// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.IO.Compression;
using System.Text;
using System.Text.Json;
using MailFathom.Application.Mail.Export;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Exports;
using Xunit;

namespace MailFathom.Application.UnitTests.Mail.Export;

/// <summary>What one archive holds, read back from the bytes rather than from the writer's own bookkeeping.</summary>
/// <remarks>
/// A person leaving the product opens this archive in whatever their operating system gives them, so every assertion
/// here reads it as an ordinary zip: the entries by name, and a message's content compared against the stored bytes it
/// was written from.
/// </remarks>
public sealed class MailboxExportArchiveWriterTests
{
    private static readonly DateTimeOffset ReceivedAt = new(2023, 11, 14, 22, 13, 20, TimeSpan.Zero);

    private static readonly MailboxExportFolder Inbox = new("INBOX", ["INBOX"], IsInbox: true);

    [Fact]
    public async Task WriteMessageAsync_AStoredMessage_CarriesItsBytesThroughUnchanged()
    {
        // Arrange
        var stored = Encoding.ASCII.GetBytes("From: sender@example.test\r\nSubject: Stored\r\n\r\nBody.\r\n");
        var destination = new MemoryStream();

        // Act
        await using (var writer = new MailboxExportArchiveWriter(destination))
        {
            var directory = writer.OpenFolder(Inbox);
            await writer.WriteMessageAsync(
                directory,
                MessageIn(Inbox, stored.Length),
                stored,
                ordinal: 7,
                TestContext.Current.CancellationToken);

            await writer.WriteKeywordsAsync(TestContext.Current.CancellationToken);
        }

        // Assert
        using var archive = OpenArchive(destination);
        var entry = Assert.Single(
            archive.Entries,
            held => held.FullName.StartsWith("cur/", StringComparison.Ordinal) && !held.FullName.EndsWith('/'));

        Assert.Equal(stored, await ReadAsync(entry));
    }

    [Fact]
    public async Task WriteMessageAsync_TheInbox_WritesItAsTheRootMaildirWithItsThreeSubdirectories()
    {
        // Arrange
        var destination = new MemoryStream();

        // Act
        await using (var writer = new MailboxExportArchiveWriter(destination))
        {
            writer.OpenFolder(Inbox);
            await writer.WriteKeywordsAsync(TestContext.Current.CancellationToken);
        }

        // Assert
        using var archive = OpenArchive(destination);

        Assert.Equal(
            ["cur/", "keywords.json", "new/", "tmp/"],
            archive.Entries.Select(entry => entry.FullName).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task WriteMessageAsync_ANestedFolder_WritesOneMaildirPlusPlusDirectoryAtTheArchiveRoot()
    {
        // Arrange
        var projects = new MailboxExportFolder("Projects/MailFathom", ["Projects", "MailFathom"], IsInbox: false);
        var destination = new MemoryStream();

        // Act
        await using (var writer = new MailboxExportArchiveWriter(destination))
        {
            var directory = writer.OpenFolder(projects);
            await writer.WriteMessageAsync(
                directory,
                MessageIn(projects, byteLength: 4),
                new byte[] { 1, 2, 3, 4 },
                ordinal: 0,
                TestContext.Current.CancellationToken);

            await writer.WriteKeywordsAsync(TestContext.Current.CancellationToken);
        }

        // Assert
        using var archive = OpenArchive(destination);

        Assert.Equal(
            [".Projects.MailFathom/cur/", ".Projects.MailFathom/new/", ".Projects.MailFathom/tmp/"],
            archive.Entries
                .Select(entry => entry.FullName)
                .Where(name => name.EndsWith('/'))
                .Order(StringComparer.Ordinal));

        Assert.Single(
            archive.Entries,
            entry => entry.FullName.StartsWith(".Projects.MailFathom/cur/", StringComparison.Ordinal)
                && !entry.FullName.EndsWith('/'));
    }

    /// <summary>A folder name is untrusted text, and nothing an extractor reads as structure may survive into a path.</summary>
    [Theory]
    [InlineData("..", ".%2E%2E/cur/")]
    [InlineData("a/b", ".a%2Fb/cur/")]
    [InlineData("a\\b", ".a%5Cb/cur/")]
    [InlineData("Zamówienia", ".Zam%C3%B3wienia/cur/")]
    public async Task OpenFolder_AFolderNamedWithStructureOrNonAscii_EncodesEveryByteOutsideTheUnreservedSet(
        string folderName,
        string expectedDirectory)
    {
        // Arrange
        var folder = new MailboxExportFolder(folderName, [folderName], IsInbox: false);
        var destination = new MemoryStream();

        // Act
        await using (var writer = new MailboxExportArchiveWriter(destination))
        {
            writer.OpenFolder(folder);
            await writer.WriteKeywordsAsync(TestContext.Current.CancellationToken);
        }

        // Assert
        using var archive = OpenArchive(destination);

        Assert.Contains(expectedDirectory, archive.Entries.Select(entry => entry.FullName), StringComparer.Ordinal);
    }

    [Fact]
    public async Task WriteMessageAsync_AMessageCarryingFlags_NamesTheFileWithItsArrivalLengthAndFlagLetters()
    {
        // Arrange
        var stored = new byte[4096];
        var destination = new MemoryStream();
        var message = MessageIn(Inbox, stored.Length) with
        {
            Flags = MaildirFlagSet.Seen | MaildirFlagSet.Draft | MaildirFlagSet.Answered,
        };

        // Act
        await using (var writer = new MailboxExportArchiveWriter(destination))
        {
            var directory = writer.OpenFolder(Inbox);
            await writer.WriteMessageAsync(
                directory,
                message,
                stored,
                ordinal: 7,
                TestContext.Current.CancellationToken);

            await writer.WriteKeywordsAsync(TestContext.Current.CancellationToken);
        }

        // Assert
        using var archive = OpenArchive(destination);

        Assert.Contains(
            "cur/1700000000.7.mailfathom,S=4096:2,DRS",
            archive.Entries.Select(entry => entry.FullName),
            StringComparer.Ordinal);
    }

    [Fact]
    public async Task WriteKeywordsAsync_MessagesCarryingKeywords_NamesEachOneAgainstThePathItWasWrittenAt()
    {
        // Arrange
        var destination = new MemoryStream();
        var keyworded = MessageIn(Inbox, byteLength: 2) with { Keywords = ["Invoices", "$Important"] };
        string writtenPath;

        // Act
        await using (var writer = new MailboxExportArchiveWriter(destination))
        {
            var directory = writer.OpenFolder(Inbox);

            writtenPath = await writer.WriteMessageAsync(
                directory,
                keyworded,
                new byte[] { 1, 2 },
                ordinal: 0,
                TestContext.Current.CancellationToken);

            await writer.WriteMessageAsync(
                directory,
                MessageIn(Inbox, byteLength: 2),
                new byte[] { 3, 4 },
                ordinal: 1,
                TestContext.Current.CancellationToken);

            await writer.WriteKeywordsAsync(TestContext.Current.CancellationToken);
        }

        // Assert
        using var archive = OpenArchive(destination);
        var document = JsonDocument.Parse(await ReadAsync(EntryNamed(archive, "keywords.json")));
        var named = document.RootElement.GetProperty("messages");

        var only = Assert.Single(named.EnumerateArray());
        Assert.Equal(writtenPath, only.GetProperty("path").GetString());
        Assert.Equal(
            ["Invoices", "$Important"],
            only.GetProperty("keywords").EnumerateArray().Select(keyword => keyword.GetString()));
    }

    /// <summary>An empty document says the export found no keyword, which a missing one does not.</summary>
    [Fact]
    public async Task WriteKeywordsAsync_AMailboxCarryingNoKeyword_StillWritesTheDocument()
    {
        // Arrange
        var destination = new MemoryStream();

        // Act
        await using (var writer = new MailboxExportArchiveWriter(destination))
        {
            writer.OpenFolder(Inbox);
            await writer.WriteKeywordsAsync(TestContext.Current.CancellationToken);
        }

        // Assert
        using var archive = OpenArchive(destination);
        var document = JsonDocument.Parse(await ReadAsync(EntryNamed(archive, "keywords.json")));

        Assert.Empty(document.RootElement.GetProperty("messages").EnumerateArray());
    }

    private static ExportableMessage MessageIn(MailboxExportFolder folder, long byteLength) => new(
        StoredEmailId.Create(Guid.Parse("0199a0c0-0000-7000-8000-000000000001")),
        folder,
        ReceivedAt,
        byteLength,
        MaildirFlagSet.None,
        []);

    private static ZipArchive OpenArchive(MemoryStream destination) =>
        new(new MemoryStream(destination.ToArray(), writable: false), ZipArchiveMode.Read);

    private static ZipArchiveEntry EntryNamed(ZipArchive archive, string name) =>
        Assert.Single(archive.Entries, entry => string.Equals(entry.FullName, name, StringComparison.Ordinal));

    private static async Task<byte[]> ReadAsync(ZipArchiveEntry entry)
    {
        await using var content = entry.Open();
        using var buffer = new MemoryStream();

        await content.CopyToAsync(buffer, TestContext.Current.CancellationToken);

        return buffer.ToArray();
    }
}
