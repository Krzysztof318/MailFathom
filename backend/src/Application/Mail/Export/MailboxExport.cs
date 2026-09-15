// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.Domain.Exports;
using MailFathom.Domain.Failures;

namespace MailFathom.Application.Mail.Export;

/// <summary>One export of a mailbox: what was asked for, where it stands, and what became of its archive.</summary>
/// <param name="Id">The identity a caller follows, downloads, cancels, and deletes the export by.</param>
/// <param name="Account">The account whose mail is being carried out.</param>
/// <param name="FolderPath">The one folder the export covers, or <see langword="null" /> when it covers the whole mailbox.</param>
/// <param name="State">Where the export stands.</param>
/// <param name="RequestedAt">When the export was asked for.</param>
/// <param name="MessageCount">How many messages have reached the archive, and the final count once it is written.</param>
/// <param name="ByteCount">How many bytes of stored mail those messages carried, before the archive's own overhead.</param>
/// <param name="ArchiveByteLength">How large the finished archive is, or <see langword="null" /> while there is none.</param>
/// <param name="ObjectLocator">The content store's key for the archive, or <see langword="null" /> once there is nothing to serve.</param>
/// <param name="CompletedAt">When the archive was finished, absent until it was.</param>
/// <param name="ExpiresAt">When the archive is deleted if nobody deletes it first, absent until it exists.</param>
/// <param name="FailureCode">The stable code of what stopped a failed export, absent for every other state.</param>
/// <remarks>
/// <para>
/// The locator is the deployment's own, never a caller's: a download is served by reading it here and streaming the
/// object, so no response ever carries a key and no caller can name an object of its own.
/// </para>
/// <para>
/// The counts are the export's own progress rather than the measurement that preceded it. The measurement is an
/// estimate taken before any job existed; these are what was actually written, which is what an operator compares
/// against it when a mailbox grew while the archive was being produced.
/// </para>
/// </remarks>
public sealed record MailboxExport(
    MailboxExportId Id,
    MailAccountId Account,
    string? FolderPath,
    MailboxExportState State,
    DateTimeOffset RequestedAt,
    long MessageCount,
    long ByteCount,
    long? ArchiveByteLength,
    string? ObjectLocator,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? ExpiresAt,
    MailFathomErrorCode? FailureCode)
{
    /// <summary>Gets whether the export is still being written, which is what bounds an account to one at a time.</summary>
    public bool IsInFlight => this.State is MailboxExportState.Queued or MailboxExportState.Running;

    /// <summary>Gets whether the archive exists and may be served.</summary>
    public bool IsDownloadable => this.State is MailboxExportState.Completed && this.ObjectLocator is not null;

    /// <summary>Records an export nobody has started work on yet.</summary>
    /// <param name="id">The identity minted for it.</param>
    /// <param name="account">The account whose mail it covers.</param>
    /// <param name="folderPath">The one folder it covers, or <see langword="null" /> for the whole mailbox.</param>
    /// <param name="requestedAt">When it was asked for.</param>
    /// <returns>The queued export.</returns>
    public static MailboxExport Queued(
        MailboxExportId id,
        MailAccountId account,
        string? folderPath,
        DateTimeOffset requestedAt) => new(
            id,
            account,
            folderPath,
            MailboxExportState.Queued,
            requestedAt,
            MessageCount: 0,
            ByteCount: 0,
            ArchiveByteLength: null,
            ObjectLocator: null,
            CompletedAt: null,
            ExpiresAt: null,
            FailureCode: null);
}
