// Copyright © 2026 Krzysztof Kasprowicz

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
}
