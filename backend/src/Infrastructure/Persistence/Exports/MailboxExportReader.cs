// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Runtime.CompilerServices;
using MailFathom.Application.Mail.Export;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Exports;
using MailFathom.Domain.Folders;
using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Exports;

/// <summary>Reads what an account's stored mail would produce as an export, from the metadata rather than from any payload.</summary>
/// <remarks>
/// <para>
/// One account's folders come from one of two places, and which decides nothing else about the export. An account whose
/// mailbox MailFathom holds has a hierarchy of its own, and its folders are that hierarchy's paths; a mirrored account
/// has the folders configuration mapped, and its folders are those aliases, which have one segment each. A held
/// account's message that has not been placed in a local folder yet falls back to the binding its occurrence names, so
/// nothing is left out of an archive because a placement had not run.
/// </para>
/// <para>
/// The inbox is read differently in the two cases for the same reason: a local hierarchy records the role, and a mapped
/// folder records only the path an operator wrote — so a mirrored account's inbox is the mapping naming <c>INBOX</c>,
/// which RFC 3501 requires every server to expose under that name without regard to case.
/// </para>
/// <para>
/// Only a message whose payload this deployment holds is counted or walked. A message recorded without content — one
/// that exceeded the size limit, or one still waiting for storage headroom — is not mail MailFathom stores, so leaving
/// it out is what makes the measurement and the archive describe the same set.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class MailboxExportReader(MailFathomDbContext dbContext) : IMailboxExportReader
{
    /// <summary>The folder name RFC 3501 requires every server to expose, which is how a mapped inbox is recognized.</summary>
    private const string MandatoryInboxPath = "INBOX";

    /// <summary>How many messages one page of the walk reads, which bounds what the walk holds at once.</summary>
    /// <remarks>The rows are metadata rather than payloads — an identity, a folder, four flags, and a length — so a page of this size is kilobytes, and the payload each of them names is fetched and let go one at a time above.</remarks>
    private const int WalkPageSize = 500;

    /// <summary>The local hierarchy's delimiter, which is MailFathom's own and is what a folder path is written with.</summary>
    private const char LocalPathDelimiter = '/';

    /// <inheritdoc />
    public async Task<MailboxExportMeasurement> MeasureAsync(
        MailAccountId account,
        string? folderPath,
        CancellationToken cancellationToken)
    {
        RequireNamedAccount(account);

        var folders = await this.ReadFolderMapAsync(account, cancellationToken);

        var totals = await this.ExportableMessages(account)
            .GroupBy(message => new { message.LocalMailFolderId, message.MailFolderId })
            .Select(group => new
            {
                group.Key.LocalMailFolderId,
                group.Key.MailFolderId,
                MessageCount = group.LongCount(),
                ByteCount = group.Sum(message => message.Content!.MimeByteLength),
            })
            .ToArrayAsync(cancellationToken);

        var measured = totals
            .Select(total => new
            {
                Folder = folders.Resolve(total.LocalMailFolderId, total.MailFolderId),
                total.MessageCount,
                total.ByteCount,
            })
            .Where(total => folderPath is null || total.Folder.Path == folderPath)
            .GroupBy(total => total.Folder.Path, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new MailboxExportFolderMeasurement(
                group.Key,
                group.Sum(total => total.MessageCount),
                group.Sum(total => total.ByteCount)));

        return new MailboxExportMeasurement([.. measured]);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MailboxExportFolder>> ReadFoldersAsync(
        MailAccountId account,
        string? folderPath,
        CancellationToken cancellationToken)
    {
        RequireNamedAccount(account);

        var folders = await this.ReadFolderMapAsync(account, cancellationToken);

        return
        [
            .. folders.Declared
                .Where(folder => folderPath is null || folder.Path == folderPath)
                .OrderBy(folder => folder.Path, StringComparer.Ordinal),
        ];
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<ExportableMessage> WalkAsync(
        MailAccountId account,
        string? folderPath,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        RequireNamedAccount(account);

        var folders = await this.ReadFolderMapAsync(account, cancellationToken);
        var after = Guid.Empty;

        while (true)
        {
            var page = await this.ExportableMessages(account)
                .Where(message => message.Id.CompareTo(after) > 0)
                .OrderBy(message => message.Id)
                .Take(WalkPageSize)
                .Select(message => new WalkedRow(
                    message.Id,
                    message.LocalMailFolderId,
                    message.MailFolderId,
                    message.ReceivedAt,
                    message.Content!.MimeByteLength,
                    message.IsRemotelySeen,
                    message.IsRemotelyFlagged,
                    message.IsRemotelyAnswered,
                    message.IsRemotelyDraft,
                    message.RemoteKeywords,
                    message.StoredAt))
                .ToArrayAsync(cancellationToken);

            if (page.Length == 0)
            {
                yield break;
            }

            foreach (var row in page)
            {
                var folder = folders.Resolve(row.LocalMailFolderId, row.MailFolderId);

                if (folderPath is not null && folder.Path != folderPath)
                {
                    continue;
                }

                yield return new ExportableMessage(
                    StoredEmailId.Create(row.Id),
                    folder,
                    row.ReceivedAt ?? row.StoredAt,
                    row.ByteLength,
                    FlagsOf(row),
                    row.Keywords);
            }

            after = page[^1].Id;
        }
    }

    /// <summary>Names the messages of one account that an export carries: those this deployment holds the payload of.</summary>
    private IQueryable<StoredEmailEntity> ExportableMessages(MailAccountId account) => dbContext.StoredEmails
        .AsNoTracking()
        .Where(message => message.MailboxAccountId == account.Value)
        .Where(message => message.Content != null);

    private static MaildirFlagSet FlagsOf(WalkedRow row)
    {
        var flags = MaildirFlagSet.None;

        if (row.IsSeen)
        {
            flags |= MaildirFlagSet.Seen;
        }

        if (row.IsFlagged)
        {
            flags |= MaildirFlagSet.Flagged;
        }

        if (row.IsAnswered)
        {
            flags |= MaildirFlagSet.Answered;
        }

        if (row.IsDraft)
        {
            flags |= MaildirFlagSet.Draft;
        }

        return flags;
    }

    private async Task<ExportFolderMap> ReadFolderMapAsync(MailAccountId account, CancellationToken cancellationToken)
    {
        var local = await dbContext.LocalMailFolders
            .AsNoTracking()
            .Where(folder => folder.MailboxAccountId == account.Value && folder.ErasedAt == null)
            .Select(folder => new LocalFolderRow(folder.Id, folder.ParentId, folder.Name, folder.Role))
            .ToArrayAsync(cancellationToken);

        var mirrored = await dbContext.MailFolders
            .AsNoTracking()
            .Where(folder => folder.MailboxAccountId == account.Value)
            .Select(folder => new MirroredFolderRow(folder.Id, folder.Alias, folder.RemotePath))
            .ToArrayAsync(cancellationToken);

        return ExportFolderMap.Compose(local, mirrored);
    }

    private static void RequireNamedAccount(MailAccountId account)
    {
        if (string.IsNullOrEmpty(account.Value))
        {
            throw new ArgumentException(
                "An export is measured and walked for a named account, so an account naming nothing cannot be read.",
                nameof(account));
        }
    }

    private sealed record WalkedRow(
        Guid Id,
        Guid? LocalMailFolderId,
        long MailFolderId,
        DateTimeOffset? ReceivedAt,
        long ByteLength,
        bool IsSeen,
        bool IsFlagged,
        bool IsAnswered,
        bool IsDraft,
        IReadOnlyList<string> Keywords,
        DateTimeOffset StoredAt);

    private sealed record LocalFolderRow(Guid Id, Guid? ParentId, string Name, MailFolderSpecialUse? Role);

    private sealed record MirroredFolderRow(long Id, string Alias, string RemotePath);

    /// <summary>Turns a message's two folder references into the one folder the archive writes it under.</summary>
    private sealed class ExportFolderMap
    {
        private readonly Dictionary<Guid, MailboxExportFolder> local;
        private readonly Dictionary<long, MailboxExportFolder> mirrored;

        private ExportFolderMap(
            Dictionary<Guid, MailboxExportFolder> local,
            Dictionary<long, MailboxExportFolder> mirrored,
            IReadOnlyList<MailboxExportFolder> declared)
        {
            this.local = local;
            this.mirrored = mirrored;
            this.Declared = declared;
        }

        /// <summary>Gets the folders the archive carries a Maildir for, whether or not they hold anything.</summary>
        public IReadOnlyList<MailboxExportFolder> Declared { get; }

        public static ExportFolderMap Compose(
            IReadOnlyList<LocalFolderRow> localRows,
            IReadOnlyList<MirroredFolderRow> mirroredRows)
        {
            var byId = localRows.ToDictionary(row => row.Id);

            var local = localRows.ToDictionary(
                row => row.Id,
                row =>
                {
                    var segments = SegmentsOf(row, byId);

                    return new MailboxExportFolder(
                        string.Join(LocalPathDelimiter, segments),
                        segments,
                        row.Role == MailFolderSpecialUse.Inbox);
                });

            var mirrored = mirroredRows.ToDictionary(
                row => row.Id,
                row => new MailboxExportFolder(
                    row.Alias,
                    [row.Alias],
                    StringComparer.OrdinalIgnoreCase.Equals(row.RemotePath, MandatoryInboxPath)));

            // A held account's own hierarchy is what an archive of it describes, so the bindings behind it are not
            // declared beside it: the same mail would be offered under two folders, and the one an operator reads a
            // held mailbox by is the local one. A mirrored account has no local hierarchy at all, so its declared
            // folders are the mappings.
            IReadOnlyList<MailboxExportFolder> declared = local.Count > 0
                ? [.. local.Values]
                : [.. mirrored.Values];

            return new ExportFolderMap(local, mirrored, declared);
        }

        /// <summary>Names the folder one message is written under, preferring the local hierarchy where the account has one.</summary>
        /// <remarks>A message of a held account that no placement has reached yet still has its binding, which is what keeps it in the archive rather than out of it.</remarks>
        public MailboxExportFolder Resolve(Guid? localFolderId, long mirroredFolderId)
        {
            if (localFolderId is { } id && this.local.TryGetValue(id, out var folder))
            {
                return folder;
            }

            return this.mirrored.TryGetValue(mirroredFolderId, out var binding)
                ? binding
                : UnknownFolder;
        }

        /// <summary>The folder a message whose own folder has gone is written under, so a walk never drops a message.</summary>
        /// <remarks>
        /// Reachable only where a folder was erased between the folder read and the walk. The name is MailFathom's own
        /// and carries nothing a person or a server chose, which is what keeps it safe to write as a path.
        /// </remarks>
        private static MailboxExportFolder UnknownFolder { get; } = new("unfiled", ["unfiled"], IsInbox: false);

        private static List<string> SegmentsOf(
            LocalFolderRow folder,
            IReadOnlyDictionary<Guid, LocalFolderRow> byId)
        {
            var segments = new List<string>();
            var current = folder;

            // Bounded by the hierarchy's own depth, which the local folder tree refuses to exceed; the visited set is
            // what keeps a row whose parent chain was corrupted from walking forever rather than reporting a folder.
            var visited = new HashSet<Guid>();

            while (visited.Add(current.Id))
            {
                segments.Add(current.Name);

                if (current.ParentId is not { } parentId || !byId.TryGetValue(parentId, out var parent))
                {
                    break;
                }

                current = parent;
            }

            segments.Reverse();

            return segments;
        }
    }
}
