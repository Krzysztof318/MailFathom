// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Jobs;
using MailFathom.Application.Jobs.Execution;
using MailFathom.Application.Jobs.Payloads;
using MailFathom.Domain.Access;
using MailFathom.Domain.Exports;
using MailFathom.Domain.Failures;

namespace MailFathom.Application.Mail.Export;

/// <summary>Writes one export's archive, streaming each stored message into it and holding none of the mailbox.</summary>
/// <remarks>
/// <para>
/// One attempt writes the whole archive rather than a chain of bounded passes, which is the opposite of every other
/// long job here and for one reason: a zip is one object with one central directory at its end, so a pass that handed
/// the rest to its successor would have to hand over a half-written object. An attempt that stops abandons the write,
/// the store is left holding nothing under the key, and the export is asked for again.
/// </para>
/// <para>
/// It reads the export's record at a checkpoint, once every hundred messages, so an operator's cancellation reaches it
/// within a hundred messages rather than at the end of a mailbox. The size limit is asked before every message instead
/// of at that checkpoint, because a message admitted past the bound is already in the archive by the time the next
/// checkpoint could refuse it.
/// </para>
/// <para>
/// Nothing here holds more than one message: the walk hands out identities, the content store answers one payload, and
/// the archive writer writes it through and lets it go.
/// </para>
/// </remarks>
public sealed class MailboxExportHandler : IJobHandler
{
    /// <summary>How many messages are written between two reads of the export's record.</summary>
    /// <remarks>
    /// The interval decides two things at once: how soon a cancellation is noticed, and how often the walk is
    /// interrupted by a row read. A hundred messages is a second or two of writing on any mailbox, which is soon enough
    /// for an operator and rare enough not to price the export at one query per message.
    /// </remarks>
    private const int MessagesBetweenCheckpoints = 100;

    private readonly IMailboxExportStore exports;
    private readonly IMailboxExportReader reader;
    private readonly IMailboxExportArchiveStore archives;
    private readonly IEmailContentStore content;
    private readonly IMailboxExportAuditor auditor;
    private readonly MailboxExportSettings settings;
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes the handler.</summary>
    /// <param name="exports">Reads the export and records what became of it.</param>
    /// <param name="reader">Walks the folders and the messages the archive is written from.</param>
    /// <param name="archives">Opens the write the archive is produced into.</param>
    /// <param name="content">Reads one stored payload at a time.</param>
    /// <param name="auditor">Records that the archive was written.</param>
    /// <param name="settings">Bounds how large the archive may be and how long it is kept.</param>
    /// <param name="timeProvider">Stamps the completion and the retention period.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public MailboxExportHandler(
        IMailboxExportStore exports,
        IMailboxExportReader reader,
        IMailboxExportArchiveStore archives,
        IEmailContentStore content,
        IMailboxExportAuditor auditor,
        MailboxExportSettings settings,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(exports);
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(archives);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(auditor);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.exports = exports;
        this.reader = reader;
        this.archives = archives;
        this.content = content;
        this.auditor = auditor;
        this.settings = settings;
        this.timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public JobType JobType => JobType.ExportMailbox;

    /// <inheritdoc />
    /// <exception cref="ArgumentException">Thrown when the payload is not the contract this job type names.</exception>
    public async Task RunAsync(IJobPayload payload, CancellationToken cancellationToken)
    {
        if (payload is not ExportMailboxJobPayload named)
        {
            throw new ArgumentException(
                $"An '{JobType.ExportMailbox}' job carries a payload naming an account and the export to write.",
                nameof(payload));
        }

        var export = await this.exports.FindAsync(named.Account, named.Export, cancellationToken);

        if (export is null || export.State is not MailboxExportState.Queued)
        {
            // Cancelled before anything started, already written by an attempt whose outcome was lost, or a record an
            // erasure removed. Each of the three is a reason to do nothing rather than to fail: at least-once execution
            // is what makes this reachable at all.
            return;
        }

        if (!await this.exports.SaveAsync(
            export with { State = MailboxExportState.Running },
            MailboxExportState.Queued,
            cancellationToken))
        {
            return;
        }

        try
        {
            await this.WriteArchiveAsync(export, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Shutdown or a lost lease. The write was abandoned with the archive, so the export goes back to queued and
            // the next attempt writes it from the beginning rather than resuming into a half-written object.
            await this.exports.SaveAsync(
                export with { State = MailboxExportState.Queued },
                MailboxExportState.Running,
                CancellationToken.None);

            throw;
        }
        catch (Exception failure)
        {
            await this.exports.SaveAsync(
                export with
                {
                    State = MailboxExportState.Failed,
                    FailureCode = (failure as MailFathomException)?.ErrorCode
                        ?? MailFathomErrorCode.MailboxExportArchiveIncomplete,
                },
                MailboxExportState.Running,
                CancellationToken.None);

            throw;
        }
    }

    private async Task WriteArchiveAsync(MailboxExport export, CancellationToken cancellationToken)
    {
        await using var write = await this.archives.BeginWriteAsync(export.Id, cancellationToken);
        var progress = new ArchiveProgress();

        await using (var writer = new MailboxExportArchiveWriter(write.Content))
        {
            var directories = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var folder in await this.reader.ReadFoldersAsync(
                export.Account,
                export.FolderPath,
                cancellationToken))
            {
                directories[folder.Path] = writer.OpenFolder(folder);
            }

            await foreach (var message in this.reader
                .WalkAsync(export.Account, export.FolderPath, cancellationToken)
                .WithCancellation(cancellationToken))
            {
                if (!directories.TryGetValue(message.Folder.Path, out var directory))
                {
                    // A folder created between the folder read and the walk. Its Maildir is written now rather than the
                    // message being dropped: what the archive owes is every message, and an empty folder read earlier is
                    // a shape rather than a promise about what follows.
                    directory = writer.OpenFolder(message.Folder);
                    directories[message.Folder.Path] = directory;
                }

                var stored = await this.content.FindStoredContentAsync(message.Message, cancellationToken);

                if (stored is null)
                {
                    // The walk names only messages whose payload this deployment holds, so this is a payload erased
                    // between the two. Nothing is written for it and nothing is counted, which is the honest answer: the
                    // mail is gone rather than left out of an archive that holds it.
                    continue;
                }

                // Asked before the message is written rather than at the next checkpoint. The bound is on what the
                // archive carries, so a check a hundred messages late is a bound on what the archive carried a hundred
                // messages ago — it would admit everything in between and abort afterwards, having already sent it to
                // the endpoint. Asked here, the export stops at the message that would cross the limit and the figure
                // the refusal names is the one the archive would have reached.
                if (progress.ByteCount + stored.RawMime.Length > this.settings.MaximumArchiveByteLength)
                {
                    throw MailboxExportRefusedException.TooLarge(
                        progress.ByteCount + stored.RawMime.Length,
                        this.settings.MaximumArchiveByteLength);
                }

                await writer.WriteMessageAsync(
                    directory,
                    message,
                    stored.RawMime,
                    progress.MessageCount,
                    cancellationToken);

                progress.Add(stored.RawMime.Length);

                if (progress.MessageCount % MessagesBetweenCheckpoints != 0)
                {
                    continue;
                }

                await this.exports.SaveProgressAsync(
                    export.Id,
                    progress.MessageCount,
                    progress.ByteCount,
                    cancellationToken);

                if (await this.WasCancelledAsync(export, cancellationToken))
                {
                    return;
                }
            }

            await writer.WriteKeywordsAsync(cancellationToken);
        }

        var archiveByteLength = await write.CompleteAsync(cancellationToken);
        var completedAt = this.timeProvider.GetUtcNow();

        var completed = export with
        {
            State = MailboxExportState.Completed,
            MessageCount = progress.MessageCount,
            ByteCount = progress.ByteCount,
            ArchiveByteLength = archiveByteLength,
            ObjectLocator = write.ObjectLocator,
            CompletedAt = completedAt,
            ExpiresAt = completedAt + this.settings.Retention,
        };

        if (!await this.exports.SaveAsync(completed, MailboxExportState.Running, cancellationToken))
        {
            // Cancelled while the archive was being finished. The object exists and nothing points at it, so it is
            // removed here rather than left to the reclamation, which is the promise a cancellation makes.
            await this.archives.DeleteAsync(write.ObjectLocator, CancellationToken.None);

            return;
        }

        await this.auditor.RecordAsync(
            new MailboxExportAct(
                Caller: "mailfathom",
                MailFathomPermission.AdminExport,
                MailboxExportActKind.Completed,
                export.Account,
                export.FolderPath,
                export.Id,
                progress.MessageCount,
                progress.ByteCount,
                completedAt),
            cancellationToken);
    }

    private async Task<bool> WasCancelledAsync(MailboxExport export, CancellationToken cancellationToken)
    {
        var current = await this.exports.FindAsync(export.Account, export.Id, cancellationToken);

        return current is null || current.State is not MailboxExportState.Running;
    }

    /// <summary>What has reached the archive so far, which is the one piece of state a run carries.</summary>
    private sealed class ArchiveProgress
    {
        public long MessageCount { get; private set; }

        public long ByteCount { get; private set; }

        public void Add(long byteLength)
        {
            this.MessageCount++;
            this.ByteCount += byteLength;
        }
    }
}
