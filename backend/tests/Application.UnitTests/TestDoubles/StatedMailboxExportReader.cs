// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Mail.Export;
using MailFathom.Domain.Accounts;

namespace MailFathom.Application.UnitTests.TestDoubles;

/// <summary>Answers with the folders and the messages a test stated, rather than reading a database.</summary>
/// <remarks>
/// The measurement is summed from the stated messages rather than given separately, so a test cannot arrange a
/// measurement that disagrees with what the walk would carry — which is the one disagreement the size limit is about.
/// </remarks>
internal sealed class StatedMailboxExportReader : IMailboxExportReader
{
    private readonly List<ExportableMessage> messages = [];
    private readonly List<MailboxExportFolder> folders = [];

    /// <summary>Gets the folder path every call was narrowed by, in the order it was asked.</summary>
    internal List<string?> AskedFolderPaths { get; } = [];

    /// <summary>States one folder the scope holds, whether or not it holds mail.</summary>
    /// <param name="folder">The folder.</param>
    /// <returns>The same reader, so an arrangement reads as one expression.</returns>
    internal StatedMailboxExportReader Holding(MailboxExportFolder folder)
    {
        this.folders.Add(folder);

        return this;
    }

    /// <summary>States one message the scope holds, and the folder it is in.</summary>
    /// <param name="message">The message.</param>
    /// <returns>The same reader, so an arrangement reads as one expression.</returns>
    internal StatedMailboxExportReader Holding(ExportableMessage message)
    {
        if (!this.folders.Contains(message.Folder))
        {
            this.folders.Add(message.Folder);
        }

        this.messages.Add(message);

        return this;
    }

    /// <inheritdoc />
    public Task<MailboxExportMeasurement> MeasureAsync(
        MailAccountId account,
        string? folderPath,
        CancellationToken cancellationToken)
    {
        this.AskedFolderPaths.Add(folderPath);

        return Task.FromResult(new MailboxExportMeasurement(
        [
            .. this.Scope(folderPath)
                .GroupBy(message => message.Folder.Path, StringComparer.Ordinal)
                .Select(folder => new MailboxExportFolderMeasurement(
                    folder.Key,
                    folder.Count(),
                    folder.Sum(message => message.ByteLength))),
        ]));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<MailboxExportFolder>> ReadFoldersAsync(
        MailAccountId account,
        string? folderPath,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<MailboxExportFolder>>(
        [
            .. this.folders.Where(folder =>
                folderPath is null || string.Equals(folder.Path, folderPath, StringComparison.Ordinal)),
        ]);

    /// <inheritdoc />
    public async IAsyncEnumerable<ExportableMessage> WalkAsync(
        MailAccountId account,
        string? folderPath,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (var message in this.Scope(folderPath))
        {
            cancellationToken.ThrowIfCancellationRequested();

            yield return message;
        }

        await Task.CompletedTask;
    }

    private IEnumerable<ExportableMessage> Scope(string? folderPath) => this.messages.Where(message =>
        folderPath is null || string.Equals(message.Folder.Path, folderPath, StringComparison.Ordinal));
}
