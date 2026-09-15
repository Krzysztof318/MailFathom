// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using System.Text;

namespace MailFathom.Domain.Exports;

/// <summary>Composes every path an exported archive holds, from names nothing in the archive may be led outside by.</summary>
/// <remarks>
/// <para>
/// A folder name is untrusted text: a person typed it, or a remote server chose it. So no name reaches a path as it was
/// written. The archive uses the Maildir++ layout — the inbox is the root Maildir and every other folder is one
/// directory at the root named <c>.</c> followed by its path segments joined by <c>.</c> — and each segment is written
/// as its UTF-8 bytes with every byte outside ASCII letters, digits, <c>-</c>, and <c>_</c> percent-encoded. A segment of
/// <c>.</c> or <c>..</c> is therefore <c>%2E</c> or <c>%2E%2E</c>, no folder directory can be named <c>cur</c>,
/// <c>new</c>, or <c>tmp</c> because every one of them begins with <c>.</c>, and a delimiter of any extractor's — a
/// slash, a backslash, a control character — cannot appear literally.
/// See <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0034-holding-a-mailbox-mailfathom-alone-keeps.md">ADR 0034</see>.
/// </para>
/// <para>
/// A message's file name is minted here and takes nothing from the message or from its folder, which is the other half
/// of the same rule: the only untrusted text an archive carries is inside a message file, where an extractor writes it
/// nowhere.
/// </para>
/// </remarks>
public static class MaildirArchiveLayout
{
    /// <summary>The document at the archive root that carries the keywords Maildir file names have no form for.</summary>
    public const string KeywordsDocumentPath = "keywords.json";

    /// <summary>The subdirectory of a Maildir that holds messages whose flags have been decided.</summary>
    private const string DeliveredSubdirectory = "cur";

    /// <summary>The token a message file name carries in place of a delivering host, which is a name no export reveals.</summary>
    private const string MintedHostToken = "mailfathom";

    /// <summary>Composes the directory one folder's Maildir sits at, relative to the archive root.</summary>
    /// <param name="pathSegments">The folder's own path, outermost segment first, exactly as the account holds it.</param>
    /// <param name="isInbox">Whether this folder is the account's inbox, which is the root Maildir rather than a directory beside it.</param>
    /// <returns>The empty string for the inbox, and otherwise the encoded <c>.</c>-prefixed directory name.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="pathSegments" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="pathSegments" /> is empty for a folder that is not the inbox.</exception>
    public static string FolderDirectory(IReadOnlyList<string> pathSegments, bool isInbox)
    {
        ArgumentNullException.ThrowIfNull(pathSegments);

        if (isInbox)
        {
            return string.Empty;
        }

        if (pathSegments.Count == 0)
        {
            throw new ArgumentException("A folder that is not the inbox has at least one path segment.", nameof(pathSegments));
        }

        return "." + string.Join('.', pathSegments.Select(EncodeSegment));
    }

    /// <summary>Composes the whole path one message is written at, inside its folder's Maildir.</summary>
    /// <param name="folderDirectory">What <see cref="FolderDirectory" /> answered for the folder the message is in.</param>
    /// <param name="fileName">What <see cref="MessageFileName" /> minted for the message.</param>
    /// <returns>A relative path under the archive root, which is what every entry of the archive is.</returns>
    /// <exception cref="ArgumentNullException">Thrown when either argument is <see langword="null" />.</exception>
    public static string MessagePath(string folderDirectory, string fileName)
    {
        ArgumentNullException.ThrowIfNull(folderDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        return folderDirectory.Length == 0
            ? $"{DeliveredSubdirectory}/{fileName}"
            : $"{folderDirectory}/{DeliveredSubdirectory}/{fileName}";
    }

    /// <summary>Mints the file name one message is written under, carrying the flags Maildir has a form for.</summary>
    /// <param name="receivedAt">When the message arrived, which is the timestamp a Maildir name begins with.</param>
    /// <param name="ordinal">The message's position in this export, which makes the name unique without a counter shared between folders.</param>
    /// <param name="byteLength">How many bytes the message holds, which the <c>S=</c> field of the name reports.</param>
    /// <param name="flags">The flags the message carries.</param>
    /// <returns>A file name of the form <c>&lt;seconds&gt;.&lt;ordinal&gt;.mailfathom,S=&lt;size&gt;:2,&lt;flags&gt;</c>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="ordinal" /> is negative or <paramref name="byteLength" /> is negative.</exception>
    /// <remarks>
    /// The flag letters are written in ascending ASCII order, which is what the Maildir convention asks of them and what
    /// makes one message's name the same however the flags were read. The host field is a constant rather than this
    /// deployment's own name: a host name in an archive a person carries away is a fact about the operator that the
    /// export has no reason to disclose.
    /// </remarks>
    public static string MessageFileName(
        DateTimeOffset receivedAt,
        long ordinal,
        long byteLength,
        MaildirFlagSet flags)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(ordinal);
        ArgumentOutOfRangeException.ThrowIfNegative(byteLength);

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{receivedAt.ToUnixTimeSeconds()}.{ordinal}.{MintedHostToken},S={byteLength}:2,{FlagLettersOf(flags)}");
    }

    /// <summary>Writes one path segment so that nothing an extractor reads as structure survives in it.</summary>
    /// <param name="segment">The folder name segment, as the account holds it.</param>
    /// <returns>The segment's UTF-8 bytes with every byte outside ASCII letters, digits, <c>-</c>, and <c>_</c> percent-encoded.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="segment" /> is <see langword="null" />.</exception>
    public static string EncodeSegment(string segment)
    {
        ArgumentNullException.ThrowIfNull(segment);

        var builder = new StringBuilder(segment.Length);

        foreach (var value in Encoding.UTF8.GetBytes(segment))
        {
            if (IsUnreserved(value))
            {
                builder.Append((char)value);
            }
            else
            {
                builder.Append(CultureInfo.InvariantCulture, $"%{value:X2}");
            }
        }

        return builder.ToString();
    }

    private static bool IsUnreserved(byte value) =>
        value is >= (byte)'a' and <= (byte)'z'
        || value is >= (byte)'A' and <= (byte)'Z'
        || value is >= (byte)'0' and <= (byte)'9'
        || value is (byte)'-' or (byte)'_';

    private static string FlagLettersOf(MaildirFlagSet flags)
    {
        // Ascending ASCII order, which is D, F, R, S.
        var letters = new StringBuilder(4);

        if (flags.HasFlag(MaildirFlagSet.Draft))
        {
            letters.Append('D');
        }

        if (flags.HasFlag(MaildirFlagSet.Flagged))
        {
            letters.Append('F');
        }

        if (flags.HasFlag(MaildirFlagSet.Answered))
        {
            letters.Append('R');
        }

        if (flags.HasFlag(MaildirFlagSet.Seen))
        {
            letters.Append('S');
        }

        return letters.ToString();
    }
}
