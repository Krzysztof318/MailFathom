// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using System.Text.Json.Serialization;

namespace MailFathom.Cli.Administration.Exports;

/// <summary>What an export of a mailbox, or of one folder of it, would carry.</summary>
/// <param name="MessageCount">How many messages the export would hold.</param>
/// <param name="ByteCount">How many bytes of stored mail those messages hold between them.</param>
/// <param name="Folders">The same two figures per folder.</param>
internal sealed record MailboxExportMeasurement(
    [property: JsonPropertyName("messageCount")] long MessageCount,
    [property: JsonPropertyName("byteCount")] long ByteCount,
    [property: JsonPropertyName("folders")] IReadOnlyList<MailboxExportFolderMeasurement>? Folders)
{
    /// <summary>Describes what the export would carry, in counts and volume rather than in an estimate of time.</summary>
    /// <returns>The figures, grouped invariantly for the reason every other figure this tool prints is.</returns>
    /// <remarks>
    /// No estimate of how long writing it would take. What an archive costs depends on how well the mail compresses and
    /// on what else the deployment is doing, so a figure invented here would be a prediction rather than a reading.
    /// </remarks>
    internal string Describe() => string.Create(
        CultureInfo.InvariantCulture,
        $"{this.MessageCount:N0} messages carrying {this.ByteCount:N0} bytes of stored mail");
}

/// <summary>What one folder contributes to an export.</summary>
/// <param name="Path">The deployment's own local path for the folder.</param>
/// <param name="MessageCount">How many messages it holds.</param>
/// <param name="ByteCount">How many bytes of stored mail they hold.</param>
internal sealed record MailboxExportFolderMeasurement(
    [property: JsonPropertyName("path")] string? Path,
    [property: JsonPropertyName("messageCount")] long MessageCount,
    [property: JsonPropertyName("byteCount")] long ByteCount);

/// <summary>Where one export stands.</summary>
/// <param name="Id">The export's identity, which every later command names it by.</param>
/// <param name="Account">The account whose mailbox it covers.</param>
/// <param name="Folder">The one folder it covers, or <see langword="null" /> for the whole mailbox.</param>
/// <param name="State">What the export is doing, as the deployment's own word for it.</param>
/// <param name="RequestedAt">When it was asked for.</param>
/// <param name="MessageCount">How many messages it has written, or measured where it has written none yet.</param>
/// <param name="ByteCount">How many bytes of stored mail those messages hold.</param>
/// <param name="ArchiveByteLength">How large the finished archive is, absent while there is none.</param>
/// <param name="CompletedAt">When the archive was finished, absent until it is.</param>
/// <param name="ExpiresAt">When the deployment deletes the archive if nobody deletes it first.</param>
/// <param name="FailureCode">The stable code of what stopped a failed export, absent for every other state.</param>
internal sealed record MailboxExport(
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("account")] string? Account,
    [property: JsonPropertyName("folder")] string? Folder,
    [property: JsonPropertyName("state")] string? State,
    [property: JsonPropertyName("requestedAt")] DateTimeOffset RequestedAt,
    [property: JsonPropertyName("messageCount")] long MessageCount,
    [property: JsonPropertyName("byteCount")] long ByteCount,
    [property: JsonPropertyName("archiveByteLength")] long? ArchiveByteLength,
    [property: JsonPropertyName("completedAt")] DateTimeOffset? CompletedAt,
    [property: JsonPropertyName("expiresAt")] DateTimeOffset? ExpiresAt,
    [property: JsonPropertyName("failureCode")] int? FailureCode)
{
    /// <summary>The deployment's word for an export whose archive is finished and downloadable.</summary>
    internal const string CompletedName = "Completed";

    /// <summary>Describes where the export stands in one line an operator reads.</summary>
    /// <returns>What it is doing, and what stopped it where something did.</returns>
    internal string DescribeState() => this.State switch
    {
        "Queued" => "queued",
        "Running" => "being written",
        CompletedName when this.CompletedAt is { } completedAt =>
            $"finished at {completedAt.ToString("u", CultureInfo.InvariantCulture)}",
        CompletedName => "finished",
        "Failed" when this.FailureCode is { } code =>
            string.Create(CultureInfo.InvariantCulture, $"failed with error {code}"),
        "Failed" => "failed",
        "Cancelled" => "cancelled",
        "Expired" => "expired, and its archive is gone",
        "Deleted" => "deleted, and its archive is gone",
        _ => this.State ?? "in a state this version of the command does not know",
    };

    /// <summary>Describes what the export has written so far, in counts and nothing derived from a message.</summary>
    /// <returns>The counts, grouped invariantly for the reason every other figure this tool prints is.</returns>
    internal string DescribeProgress() => this.ArchiveByteLength is { } archiveByteLength
        ? string.Create(
            CultureInfo.InvariantCulture,
            $"{this.MessageCount:N0} messages carrying {this.ByteCount:N0} bytes, in an archive of {archiveByteLength:N0} bytes")
        : string.Create(
            CultureInfo.InvariantCulture,
            $"{this.MessageCount:N0} messages carrying {this.ByteCount:N0} bytes");

    /// <summary>Describes when the archive goes if nobody fetches and deletes it first.</summary>
    /// <returns>The instant, or what there is to say where no archive exists.</returns>
    internal string DescribeRetention() => this.ExpiresAt is { } expiresAt
        ? $"deleted at {expiresAt.ToString("u", CultureInfo.InvariantCulture)} unless deleted sooner"
        : "no archive to keep";
}

/// <summary>The measurement an export was decided on, and the export that decision produced.</summary>
/// <param name="Measurement">What the export was measured to carry before any job existed.</param>
/// <param name="Export">The export now recorded.</param>
/// <param name="WasAlreadyRunning">Whether this is an export already being written rather than one this command started.</param>
internal sealed record MailboxExportStart(
    [property: JsonPropertyName("measurement")] MailboxExportMeasurement? Measurement,
    [property: JsonPropertyName("export")] MailboxExport? Export,
    [property: JsonPropertyName("wasAlreadyRunning")] bool WasAlreadyRunning);

/// <summary>The exports one account has, newest first.</summary>
/// <param name="Exports">The exports.</param>
internal sealed record MailboxExportListing(
    [property: JsonPropertyName("exports")] IReadOnlyList<MailboxExport>? Exports);

/// <summary>What a request to start an export names.</summary>
/// <param name="Account">The account to export, as the deployment's configuration names it.</param>
/// <param name="Folder">The one folder to export alone, or <see langword="null" /> for the whole mailbox.</param>
internal sealed record MailboxExportRequest(
    [property: JsonPropertyName("account")] string Account,
    [property: JsonPropertyName("folder")] string? Folder);
