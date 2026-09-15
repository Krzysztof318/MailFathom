// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Mail.Export;

/// <summary>What an export of one mailbox, or of one folder of it, would carry — read from what the database records rather than from any payload.</summary>
/// <param name="Folders">One row per folder that holds anything, in the order the archive would write them.</param>
/// <remarks>
/// <para>
/// The figures are the recorded lengths of the stored messages, summed. No payload is read to produce them, which is
/// what lets the answer come back in seconds for a mailbox of years — and what makes it an estimate rather than a
/// promise: the archive adds a header and a directory entry per message, and mail arriving between the measurement and
/// the job is exported too.
/// </para>
/// <para>
/// A folder holding nothing is left out rather than reported as a zero, because what an operator reads this for is
/// where the volume is.
/// </para>
/// </remarks>
public sealed record MailboxExportMeasurement(IReadOnlyList<MailboxExportFolderMeasurement> Folders)
{
    /// <summary>Gets the measurement of a mailbox, or a scope of one, that holds no stored message at all.</summary>
    public static MailboxExportMeasurement Empty { get; } = new([]);

    /// <summary>Gets how many stored messages the export would carry.</summary>
    public long MessageCount => this.Folders.Sum(folder => folder.MessageCount);

    /// <summary>Gets how many bytes of stored mail those messages hold between them, before the archive's own overhead.</summary>
    public long ByteCount => this.Folders.Sum(folder => folder.ByteCount);
}

/// <summary>What one folder contributes to an export.</summary>
/// <param name="Path">The folder's path, in the words an operator names a folder by.</param>
/// <param name="MessageCount">How many stored messages the folder holds.</param>
/// <param name="ByteCount">The sum of their recorded lengths.</param>
public sealed record MailboxExportFolderMeasurement(string Path, long MessageCount, long ByteCount);
