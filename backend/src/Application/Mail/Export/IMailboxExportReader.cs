// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Exports;

namespace MailFathom.Application.Mail.Export;

/// <summary>Reads what an account's stored mail would produce as an export, in two shapes: the totals, and the messages themselves.</summary>
/// <remarks>
/// <para>
/// Both members take the same scope, so the figures an operator confirmed and the messages the archive is written from
/// are read from one place under one set of rules. Neither reads a payload: the measurement sums recorded lengths, and
/// the walk hands out identities the archive writer then fetches content for one message at a time.
/// </para>
/// <para>
/// The walk is an asynchronous sequence rather than a page a caller loops over, because the archive is written in one
/// pass whose whole point is that no step of it holds the mailbox. An implementation pages underneath; what this
/// contract promises above is that a message is materialized, written, and let go before the next one is read.
/// </para>
/// </remarks>
public interface IMailboxExportReader
{
    /// <summary>Sums what the scope holds, per folder and in total.</summary>
    /// <param name="account">The account whose mailbox is measured.</param>
    /// <param name="folderPath">One folder's path to measure alone, or <see langword="null" /> for the whole mailbox.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The measurement, which is empty when the scope holds no stored message.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="account" /> names nothing.</exception>
    Task<MailboxExportMeasurement> MeasureAsync(
        MailAccountId account,
        string? folderPath,
        CancellationToken cancellationToken);

    /// <summary>Names every folder of the scope that holds nothing, so the archive still carries an empty Maildir for it.</summary>
    /// <param name="account">The account whose folders are read.</param>
    /// <param name="folderPath">One folder's path to read alone, or <see langword="null" /> for the whole mailbox.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>Every folder of the scope, whether or not it holds mail.</returns>
    /// <remarks>
    /// An empty folder is part of a mailbox's shape, and a person restoring the archive onto a server expects the folder
    /// they made to still be there. It is read separately from the walk because the walk is ordered by message and a
    /// folder with no message would never appear in it.
    /// </remarks>
    /// <exception cref="ArgumentException">Thrown when <paramref name="account" /> names nothing.</exception>
    Task<IReadOnlyList<MailboxExportFolder>> ReadFoldersAsync(
        MailAccountId account,
        string? folderPath,
        CancellationToken cancellationToken);

    /// <summary>Walks the stored messages of the scope, folder by folder, in the order the archive writes them.</summary>
    /// <param name="account">The account whose mail is walked.</param>
    /// <param name="folderPath">One folder's path to walk alone, or <see langword="null" /> for the whole mailbox.</param>
    /// <param name="cancellationToken">Cancels the walk.</param>
    /// <returns>Every stored message of the scope, each with where it is and what it carries.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="account" /> names nothing.</exception>
    IAsyncEnumerable<ExportableMessage> WalkAsync(
        MailAccountId account,
        string? folderPath,
        CancellationToken cancellationToken);
}

/// <summary>One stored message as the archive writer meets it, before its payload has been read.</summary>
/// <param name="Message">The stored message's own identity, which the payload is fetched by.</param>
/// <param name="Folder">The folder it is in, which decides where in the archive it is written.</param>
/// <param name="ReceivedAt">When the message arrived, which the Maildir file name begins with.</param>
/// <param name="ByteLength">The recorded length of the stored payload, which the file name reports.</param>
/// <param name="Flags">The flags Maildir has a letter for, read from the state the account holds.</param>
/// <param name="Keywords">The keywords the message carries beside them, which the archive's keywords document holds.</param>
public sealed record ExportableMessage(
    StoredEmailId Message,
    MailboxExportFolder Folder,
    DateTimeOffset ReceivedAt,
    long ByteLength,
    MaildirFlagSet Flags,
    IReadOnlyList<string> Keywords);
