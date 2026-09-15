// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using MailFathom.Cli.Administration.Exports;
using MailFathom.Cli.Output;

namespace MailFathom.Cli.Commands.Exports;

/// <summary>Lays out what the export commands print, so six commands say one thing one way.</summary>
/// <remarks>
/// The measurement is shown by the command that only measures and again by the one that starts an export, and an
/// export's own lines are printed by five of the six. Written once because a figure an operator confirms against and
/// the figure they see afterwards have to be the same figure laid out the same way.
/// </remarks>
internal static class ExportOutput
{
    /// <summary>Lays out what an export would carry, or has carried.</summary>
    /// <param name="measurement">The measurement.</param>
    /// <returns>The lines to print.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="measurement" /> is <see langword="null" />.</exception>
    internal static CliDetails Describe(MailboxExportMeasurement measurement)
    {
        ArgumentNullException.ThrowIfNull(measurement);

        CliDetails details = new();
        details.Add("To export", measurement.Describe());

        return details;
    }

    /// <summary>Lays out one export's state, what it has written, and when its archive goes.</summary>
    /// <param name="export">The export.</param>
    /// <returns>The lines to print.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="export" /> is <see langword="null" />.</exception>
    internal static CliDetails Describe(MailboxExport export)
    {
        ArgumentNullException.ThrowIfNull(export);

        CliDetails details = new();
        details.Add("Export", export.Id ?? "unnamed");
        details.Add("State", export.DescribeState());
        details.Add("Written", export.DescribeProgress());
        details.Add("Archive", export.DescribeRetention());

        return details;
    }

    /// <summary>Lays out what each folder contributes, so an operator sees which one holds the weight.</summary>
    /// <param name="folders">The per-folder figures.</param>
    /// <returns>The table to print.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="folders" /> is <see langword="null" />.</exception>
    internal static CliTable Tabulate(IReadOnlyList<MailboxExportFolderMeasurement> folders)
    {
        ArgumentNullException.ThrowIfNull(folders);

        CliTable listing = new("Folder", "Messages", "Bytes");

        foreach (var folder in folders)
        {
            listing.AddRow(
                folder.Path ?? "unnamed",
                folder.MessageCount.ToString("N0", CultureInfo.InvariantCulture),
                folder.ByteCount.ToString("N0", CultureInfo.InvariantCulture));
        }

        return listing;
    }

    /// <summary>Lays out the exports an account has, newest first.</summary>
    /// <param name="exports">The exports.</param>
    /// <returns>The table to print.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="exports" /> is <see langword="null" />.</exception>
    internal static CliTable Tabulate(IReadOnlyList<MailboxExport> exports)
    {
        ArgumentNullException.ThrowIfNull(exports);

        CliTable listing = new("Export", "Folder", "State", "Requested", "Written", "Archive goes");

        foreach (var export in exports)
        {
            listing.AddRow(
                export.Id ?? "unnamed",
                export.Folder ?? "whole mailbox",
                export.DescribeState(),
                export.RequestedAt.ToString("u", CultureInfo.InvariantCulture),
                export.MessageCount.ToString("N0", CultureInfo.InvariantCulture),
                export.ExpiresAt?.ToString("u", CultureInfo.InvariantCulture) ?? "—");
        }

        return listing;
    }
}
