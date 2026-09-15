// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Exports;

namespace MailFathom.Application.Mail.Export;

/// <summary>Records who asked this deployment to carry a mailbox out of it, and how much of one they carried.</summary>
/// <remarks>
/// <para>
/// Every act on an export is recorded — measuring, starting, reading, cancelling, downloading, and deleting — because
/// each of them is a step of one person taking a copy of somebody's whole mailbox, and a mailbox owner asking who
/// exported their mail cannot be answered from a record that does not say.
/// </para>
/// <para>
/// <b>Nothing about a message reaches a record.</b> An account identifier, an export identity, an act, a permission,
/// and two counts are MailFathom's own names for things; every subject, address, and body stays in the stored mail the
/// archive was written from.
/// </para>
/// <para>
/// The folder path is the one value a person chose, so it is carried for a durable store to hold beside the mail it
/// names and never written to a log — where no folder path, subject, address, or UID goes. An implementation writing
/// to the log says whether the export covered the whole mailbox instead.
/// </para>
/// </remarks>
public interface IMailboxExportAuditor
{
    /// <summary>Records one act on one export.</summary>
    /// <param name="act">What was asked for, and what it reached.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes once the act is recorded.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="act" /> is <see langword="null" />.</exception>
    Task RecordAsync(MailboxExportAct act, CancellationToken cancellationToken);
}

/// <summary>One act on one export, as the record of it is written.</summary>
/// <param name="Caller">MailFathom's own name for the credential that asked, or the deployment's own process where it asked itself.</param>
/// <param name="Grant">The permission the act was admitted under.</param>
/// <param name="Kind">Which act it was.</param>
/// <param name="Account">The account whose mailbox it reached.</param>
/// <param name="FolderPath">The one folder the export covers, or <see langword="null" /> for a whole mailbox.</param>
/// <param name="Export">The export it was about, absent for a measurement, which exists before any export does.</param>
/// <param name="MessageCount">How many messages the act reached.</param>
/// <param name="ByteCount">How many bytes of stored mail they hold between them.</param>
/// <param name="OccurredAt">When it happened.</param>
public sealed record MailboxExportAct(
    string Caller,
    MailFathomPermission Grant,
    MailboxExportActKind Kind,
    MailAccountId Account,
    string? FolderPath,
    MailboxExportId? Export,
    long MessageCount,
    long ByteCount,
    DateTimeOffset OccurredAt);

/// <summary>The acts an export has, each of which is recorded.</summary>
public enum MailboxExportActKind
{
    /// <summary>Somebody asked what an export would carry, without starting one.</summary>
    Measured = 0,

    /// <summary>Somebody asked for an export, which enqueued the work that writes the archive.</summary>
    Started = 1,

    /// <summary>The archive was written whole.</summary>
    Completed = 2,

    /// <summary>Somebody read where an export stands.</summary>
    Read = 3,

    /// <summary>Somebody stopped an export, which deleted whatever it had written.</summary>
    Cancelled = 4,

    /// <summary>Somebody downloaded an archive.</summary>
    Downloaded = 5,

    /// <summary>Somebody deleted an archive before its retention ended.</summary>
    Deleted = 6,

    /// <summary>The retention period ended and the deployment deleted the archive itself.</summary>
    Expired = 7,
}
