// Copyright © 2026 Krzysztof Kasprowicz

using System.Security.Cryptography;
using System.Text;
using MailMcp.Application.EmailContent;
using MailMcp.Domain.Accounts;
using MailMcp.Domain.Emails;
using MailMcp.Domain.Folders;

namespace MailMcp.Infrastructure.UnitTests;

/// <summary>Builds the in-memory raw MIME the extraction tests parse.</summary>
/// <remarks>
/// Every fixture is written as text and turned into bytes here, so no test touches the file system and the exact header
/// each rule is about stays readable in the test that asserts on it.
/// </remarks>
internal static class MimeFixtures
{
    /// <summary>Gets the occurrence identity every fixture is fetched under.</summary>
    public static EmailOccurrenceId OccurrenceId { get; } = EmailOccurrenceId.Create(
        MailAccountId.Create("primary"),
        MailFolderResolution.FirstBindingOf(MailFolderAlias.Create("inbox"), RemoteFolderPath.Create("INBOX", '/')).Id,
        ImapUidValidity.Create(5),
        ImapUid.Create(10));

    /// <summary>Turns MIME lines into the fetched content an extraction reads.</summary>
    /// <param name="lines">The message's lines, joined with CRLF as a mail transport writes them.</param>
    /// <returns>The content, carrying a fixed occurrence identity.</returns>
    public static RemoteEmailContent Message(params string[] lines) =>
        new(OccurrenceId, Encoding.UTF8.GetBytes(string.Join("\r\n", lines)));

    /// <summary>Turns raw bytes into fetched content, for the cases that are about bytes rather than about headers.</summary>
    /// <param name="rawMime">The raw payload.</param>
    /// <returns>The content, carrying a fixed occurrence identity.</returns>
    public static RemoteEmailContent RawContent(ReadOnlyMemory<byte> rawMime) => new(OccurrenceId, rawMime);

    /// <summary>Turns MIME lines into stored content a read renders, recorded as intact.</summary>
    /// <param name="lines">The message's lines, joined with CRLF as a mail transport writes them.</param>
    /// <returns>The stored content, whose recorded length and digest describe the bytes beside them.</returns>
    /// <remarks>
    /// The recorded values are computed rather than stated, so a rendering test never fails on an integrity check it is
    /// not about. Damaged content is the read's own concern and is arranged where that behavior is asserted.
    /// </remarks>
    public static StoredEmailContent StoredMessage(params string[] lines) => StoredRawContent(
        Encoding.UTF8.GetBytes(string.Join("\r\n", lines)));

    /// <summary>Turns raw bytes into stored content, recorded as intact.</summary>
    /// <param name="rawMime">The raw payload.</param>
    /// <returns>The stored content, whose recorded length and digest describe the bytes beside them.</returns>
    public static StoredEmailContent StoredRawContent(byte[] rawMime)
    {
        ArgumentNullException.ThrowIfNull(rawMime);

        return new StoredEmailContent(rawMime, rawMime.LongLength, SHA256.HashData(rawMime));
    }
}
