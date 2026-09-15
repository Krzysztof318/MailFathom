// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using MailFathom.Application.Mail.Export;

namespace MailFathom.Host.Api;

/// <summary>What an export of a mailbox, or of one folder of it, would carry.</summary>
/// <param name="MessageCount">How many messages the export would hold.</param>
/// <param name="ByteCount">How many bytes of stored mail those messages hold between them.</param>
/// <param name="Folders">The same two figures per folder, in the order the reader answered them.</param>
/// <remarks>
/// A folder's path is the deployment's own local path for it, which is the one thing here a person chose. It is in the
/// answer because an operator deciding whether to export a whole mailbox or one folder needs to see which folder holds
/// the weight, and it reaches no log on its way there.
/// </remarks>
internal sealed record MailboxExportMeasurementResponse(
    long MessageCount,
    long ByteCount,
    IReadOnlyList<MailboxExportFolderMeasurementResponse> Folders)
{
    /// <summary>Describes a measurement.</summary>
    /// <param name="measurement">What the reader counted.</param>
    /// <returns>The response body.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="measurement" /> is <see langword="null" />.</exception>
    internal static MailboxExportMeasurementResponse For(MailboxExportMeasurement measurement)
    {
        ArgumentNullException.ThrowIfNull(measurement);

        return new MailboxExportMeasurementResponse(
            measurement.MessageCount,
            measurement.ByteCount,
            [.. measurement.Folders.Select(folder => new MailboxExportFolderMeasurementResponse(
                folder.Path,
                folder.MessageCount,
                folder.ByteCount))]);
    }
}

/// <summary>What one folder contributes to an export.</summary>
/// <param name="Path">The deployment's own local path for the folder.</param>
/// <param name="MessageCount">How many messages it holds.</param>
/// <param name="ByteCount">How many bytes of stored mail they hold.</param>
internal sealed record MailboxExportFolderMeasurementResponse(string Path, long MessageCount, long ByteCount);

/// <summary>Where one export stands.</summary>
/// <param name="Id">The export's identity, which every later act names it by.</param>
/// <param name="Account">The account whose mailbox it covers.</param>
/// <param name="Folder">The one folder it covers, or <see langword="null" /> for the whole mailbox.</param>
/// <param name="State">What the export is doing, as this repository publishes the name.</param>
/// <param name="RequestedAt">When it was asked for.</param>
/// <param name="MessageCount">How many messages it has written, or measured where it has written none yet.</param>
/// <param name="ByteCount">How many bytes of stored mail those messages hold.</param>
/// <param name="ArchiveByteLength">How large the finished archive is, absent while there is none.</param>
/// <param name="CompletedAt">When the archive was finished, absent until it is.</param>
/// <param name="ExpiresAt">When the archive is deleted if nobody deletes it first, absent while there is none.</param>
/// <param name="FailureCode">The stable code of what stopped a failed export, absent for every other state.</param>
/// <remarks>
/// The object's key is deliberately absent. It is this deployment's own name for where the archive is kept, a caller
/// never reaches the endpoint directly, and a key that left here would be the one identifier naming a person's whole
/// mailbox in a place nothing bounds.
/// </remarks>
internal sealed record MailboxExportResponse(
    string Id,
    string Account,
    string? Folder,
    string State,
    DateTimeOffset RequestedAt,
    long MessageCount,
    long ByteCount,
    long? ArchiveByteLength,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? ExpiresAt,
    int? FailureCode)
{
    /// <summary>Describes one export.</summary>
    /// <param name="export">The export.</param>
    /// <returns>The response body.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="export" /> is <see langword="null" />.</exception>
    internal static MailboxExportResponse For(MailboxExport export)
    {
        ArgumentNullException.ThrowIfNull(export);

        return new MailboxExportResponse(
            export.Id.Value.ToString("N", CultureInfo.InvariantCulture),
            export.Account.Value,
            export.FolderPath,
            export.State.ToString(),
            export.RequestedAt,
            export.MessageCount,
            export.ByteCount,
            export.ArchiveByteLength,
            export.CompletedAt,
            export.ExpiresAt,
            export.FailureCode?.Value);
    }
}

/// <summary>The measurement an export was decided on, and the export that decision produced.</summary>
/// <param name="Measurement">What the export was measured to carry before any job existed.</param>
/// <param name="Export">The export now recorded.</param>
/// <param name="WasAlreadyRunning">Whether this answer is an export already being written rather than one this request started.</param>
/// <remarks>
/// The measurement travels with the export because it is what the caller would otherwise have to ask for twice: the
/// command shows it before asking for confirmation, and shows it again beside the export it produced.
/// </remarks>
internal sealed record MailboxExportStartResponse(
    MailboxExportMeasurementResponse Measurement,
    MailboxExportResponse Export,
    bool WasAlreadyRunning)
{
    /// <summary>Describes what starting an export produced.</summary>
    /// <param name="start">The measurement and the export.</param>
    /// <returns>The response body.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="start" /> is <see langword="null" />.</exception>
    internal static MailboxExportStartResponse For(MailboxExportStart start)
    {
        ArgumentNullException.ThrowIfNull(start);

        return new MailboxExportStartResponse(
            MailboxExportMeasurementResponse.For(start.Measurement),
            MailboxExportResponse.For(start.Export),
            start.WasAlreadyRunning);
    }
}

/// <summary>The exports one account has, newest first.</summary>
/// <param name="Exports">The exports.</param>
internal sealed record MailboxExportListResponse(IReadOnlyList<MailboxExportResponse> Exports)
{
    /// <summary>Describes a listing.</summary>
    /// <param name="exports">The exports the store answered with.</param>
    /// <returns>The response body.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="exports" /> is <see langword="null" />.</exception>
    internal static MailboxExportListResponse For(IReadOnlyList<MailboxExport> exports)
    {
        ArgumentNullException.ThrowIfNull(exports);

        return new MailboxExportListResponse([.. exports.Select(MailboxExportResponse.For)]);
    }
}

/// <summary>What a request to start an export names.</summary>
/// <param name="Account">The account to export, as the deployment's own identifier for it.</param>
/// <param name="Folder">The one folder to export alone, or <see langword="null" /> for the whole mailbox.</param>
internal sealed record MailboxExportRequest(string? Account, string? Folder);
