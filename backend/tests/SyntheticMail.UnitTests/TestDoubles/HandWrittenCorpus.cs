// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.IO.Compression;
using System.Text;
using MailFathom.SyntheticMail.Corpus;

namespace MailFathom.SyntheticMail.UnitTests.TestDoubles;

/// <summary>Builds a corpus archive by hand, message by message.</summary>
/// <remarks>
/// Replay exists to deliver mail this run did not produce, so the tests that hold it to that give it a corpus this
/// repository's generator did not write either. Building one by hand is also how a manifest can be malformed on
/// purpose — a message it names that is not there, an author a message does not carry — which an exported corpus
/// never is.
/// </remarks>
internal static class HandWrittenCorpus
{
    /// <summary>Builds an archive holding one manifest and the named messages.</summary>
    /// <param name="manifest">The manifest's contents, or <see langword="null" /> to leave the archive without one.</param>
    /// <param name="messages">Each message's entry name and contents.</param>
    /// <returns>The archive, positioned at its start, which the caller disposes.</returns>
    internal static MemoryStream Build(string? manifest, params (string Name, string Contents)[] messages)
    {
        var buffer = new MemoryStream();

        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            if (manifest is not null)
            {
                WriteEntry(archive, CorpusArchive.ManifestEntryName, manifest);
            }

            foreach (var (name, contents) in messages)
            {
                WriteEntry(archive, name, contents);
            }
        }

        buffer.Position = 0;

        return buffer;
    }

    /// <summary>Writes one message the way a corpus carries it: authored, addressed to the other side, and threaded by the seed.</summary>
    /// <param name="messageId">The identifier it proposes, without angle brackets.</param>
    /// <param name="subject">Its subject.</param>
    /// <param name="from">Who wrote it.</param>
    /// <param name="to">Who it is written to.</param>
    /// <param name="inReplyTo">The identifier it answers, without angle brackets, or <see langword="null" /> when it opens the exchange.</param>
    /// <returns>The message, as an exported corpus would hold it.</returns>
    internal static string Message(string messageId, string subject, string from, string to, string? inReplyTo = null)
    {
        var ancestry = inReplyTo is null
            ? string.Empty
            : $"In-Reply-To: <{inReplyTo}>\r\nReferences: <{inReplyTo}>\r\n";

        return $"Date: Sat, 08 Aug 2026 11:30:00 +0000\r\nMessage-Id: <{messageId}>\r\nSubject: {subject}\r\n{ancestry}From: {from}\r\nTo: {to}\r\n\r\nWhatever it says.\r\n";
    }

    private static void WriteEntry(ZipArchive archive, string name, string contents)
    {
        using var entry = archive.CreateEntry(name).Open();

        entry.Write(Encoding.UTF8.GetBytes(contents));
    }
}
