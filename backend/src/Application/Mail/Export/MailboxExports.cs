// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Jobs;
using MailFathom.Application.Jobs.Payloads;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Exports;
using MailFathom.Domain.Failures;

namespace MailFathom.Application.Mail.Export;

/// <summary>Everything an operator does about carrying a mailbox out of this deployment, and none of the work that writes one.</summary>
/// <remarks>
/// <para>
/// Measuring, starting, reading, cancelling, downloading, and deleting. Only the download moves any mail, and even that
/// one hands back a stream the transport copies rather than reading an archive into this process; starting an export
/// records it and enqueues the work, so an operator's terminal neither carries the export nor keeps it alive.
/// </para>
/// <para>
/// The measurement comes before the job on purpose. An export of a mailbox of years reads every stored payload once,
/// which is the most expensive thing this deployment does to itself, and the figure that says so is answerable in
/// seconds from what the database already records. So nothing starts until the caller has been shown what it would
/// carry and the two configured bounds have admitted it.
/// </para>
/// </remarks>
public sealed class MailboxExports
{
    /// <summary>How many of an account's exports one listing reports.</summary>
    /// <remarks>A constant rather than a page, because an account has a handful of exports: one archive per mailbox is kept for two days and the rows behind it are what an operator reads a history of.</remarks>
    private const int ListingLimit = 50;

    private readonly AccessAuthorization authorization;
    private readonly IMailboxExportStore exports;
    private readonly IMailboxExportReader reader;
    private readonly IMailboxExportArchiveStore archives;
    private readonly IMailboxExportAuditor auditor;
    private readonly IJobStore jobs;
    private readonly StoredContentCeiling ceiling;
    private readonly MailboxExportSettings settings;
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes the use case.</summary>
    /// <param name="authorization">Answers which principal reached this use case, and under which grant.</param>
    /// <param name="exports">Keeps the record of every export.</param>
    /// <param name="reader">Measures what an export would carry.</param>
    /// <param name="archives">Says whether this deployment can keep an archive at all, and serves and removes the ones it has.</param>
    /// <param name="auditor">Records who asked for what.</param>
    /// <param name="jobs">Queues the work that writes an archive.</param>
    /// <param name="ceiling">Answers whether the deployment's storage has room for a second copy of the mailbox.</param>
    /// <param name="settings">Bounds how large an archive may be and how long one is kept.</param>
    /// <param name="timeProvider">Stamps each request and each retention period.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public MailboxExports(
        AccessAuthorization authorization,
        IMailboxExportStore exports,
        IMailboxExportReader reader,
        IMailboxExportArchiveStore archives,
        IMailboxExportAuditor auditor,
        IJobStore jobs,
        StoredContentCeiling ceiling,
        MailboxExportSettings settings,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentNullException.ThrowIfNull(exports);
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(archives);
        ArgumentNullException.ThrowIfNull(auditor);
        ArgumentNullException.ThrowIfNull(jobs);
        ArgumentNullException.ThrowIfNull(ceiling);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.authorization = authorization;
        this.exports = exports;
        this.reader = reader;
        this.archives = archives;
        this.auditor = auditor;
        this.jobs = jobs;
        this.ceiling = ceiling;
        this.settings = settings;
        this.timeProvider = timeProvider;
    }

    /// <summary>The configuration key a deployment turns the export on with, which the refusal names.</summary>
    public const string ObjectStorageSettingName = "ContentStorage:ObjectStorage";

    /// <summary>Reports what an export of a mailbox, or of one folder of it, would carry — without starting one.</summary>
    /// <param name="account">The account to measure.</param>
    /// <param name="folderPath">One folder's path to measure alone, or <see langword="null" /> for the whole mailbox.</param>
    /// <param name="cancellationToken">Cancels the measurement.</param>
    /// <returns>The measurement, per folder and in total.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller does not hold the export permission.</exception>
    /// <exception cref="MailboxExportRefusedException">Thrown when this deployment has nowhere to keep an archive.</exception>
    public async Task<MailboxExportMeasurement> MeasureAsync(
        MailAccountId account,
        string? folderPath,
        CancellationToken cancellationToken)
    {
        this.authorization.RequirePermission(MailFathomPermission.AdminExport);
        this.RequireSomewhereToKeepAnArchive();

        var measurement = await this.reader.MeasureAsync(account, folderPath, cancellationToken);

        await this.RecordAsync(
            MailboxExportActKind.Measured,
            account,
            folderPath,
            export: null,
            measurement.MessageCount,
            measurement.ByteCount,
            cancellationToken);

        return measurement;
    }

    /// <summary>Records an export and queues the work that writes its archive.</summary>
    /// <param name="account">The account to export.</param>
    /// <param name="folderPath">One folder's path to export alone, or <see langword="null" /> for the whole mailbox.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The measurement the decision was taken on, and the export now recorded.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller does not hold the export permission.</exception>
    /// <exception cref="MailboxExportRefusedException">Thrown when this deployment has nowhere to keep an archive, the export is past a configured bound, or the account is already exporting a different scope.</exception>
    /// <remarks>
    /// An export of the same scope that is already being written is answered with itself rather than started over, which
    /// is what makes asking twice the same as asking once. A second scope is refused instead, because an account writes
    /// one archive at a time.
    /// </remarks>
    public async Task<MailboxExportStart> StartAsync(
        MailAccountId account,
        string? folderPath,
        CancellationToken cancellationToken)
    {
        this.authorization.RequirePermission(MailFathomPermission.AdminExport);
        this.RequireSomewhereToKeepAnArchive();

        var measurement = await this.reader.MeasureAsync(account, folderPath, cancellationToken);

        var inFlight = await this.exports.FindInFlightAsync(account, cancellationToken);

        if (inFlight is not null)
        {
            return string.Equals(inFlight.FolderPath, folderPath, StringComparison.Ordinal)
                ? new MailboxExportStart(measurement, inFlight, WasAlreadyRunning: true)
                : throw MailboxExportRefusedException.AlreadyRunning();
        }

        if (measurement.ByteCount > this.settings.MaximumArchiveByteLength)
        {
            throw MailboxExportRefusedException.TooLarge(
                measurement.ByteCount,
                this.settings.MaximumArchiveByteLength);
        }

        await this.RequireStorageHeadroomAsync(account, measurement.ByteCount, cancellationToken);

        var export = MailboxExport.Queued(
            MailboxExportId.Create(Guid.CreateVersion7()),
            account,
            folderPath,
            this.timeProvider.GetUtcNow());

        await this.exports.RecordAsync(export, cancellationToken);

        // Recorded as soon as the row exists, rather than once the work is queued. The export row carries no caller,
        // so this act is the only place the answer to who asked for it is written — and the hand-on below can refuse
        // at capacity and leave the row behind as a failed export, which would then say nothing about who it belonged
        // to. Every persisted export therefore has its act before anything can go wrong with the queue.
        await this.RecordAsync(
            MailboxExportActKind.Started,
            account,
            folderPath,
            export.Id,
            measurement.MessageCount,
            measurement.ByteCount,
            cancellationToken);

        var work = ExportMailboxJobPayload.For(account, export.Id);
        var enqueued = await this.jobs.EnqueueAsync(
            JobEnqueueRequest.Create(work.ToIdempotencyKey(), work, account),
            cancellationToken);

        if (enqueued.Outcome is JobEnqueueOutcome.RefusedAtCapacity)
        {
            // The record is left as it is rather than removed: the queue being full is a fact about the deployment and
            // the export it refused is exactly what an operator reads to learn that, so it fails rather than vanishing.
            await this.exports.SaveAsync(
                export with
                {
                    State = MailboxExportState.Failed,
                    FailureCode = MailFathomErrorCode.JobHandOnRefusedAtCapacity,
                },
                MailboxExportState.Queued,
                cancellationToken);

            throw new JobHandOnRefusedAtCapacityException(JobType.ExportMailbox);
        }

        return new MailboxExportStart(measurement, export, WasAlreadyRunning: false);
    }

    /// <summary>Reads where one export stands.</summary>
    /// <param name="account">The account the export belongs to.</param>
    /// <param name="export">The export's identity.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The export.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller does not hold the export permission.</exception>
    /// <exception cref="MailboxExportRefusedException">Thrown when the account holds no export under that identity.</exception>
    public async Task<MailboxExport> ReadAsync(
        MailAccountId account,
        MailboxExportId export,
        CancellationToken cancellationToken)
    {
        this.authorization.RequirePermission(MailFathomPermission.AdminExport);

        var found = await this.RequireExportAsync(account, export, cancellationToken);

        await this.RecordAsync(
            MailboxExportActKind.Read,
            account,
            found.FolderPath,
            found.Id,
            found.MessageCount,
            found.ByteCount,
            cancellationToken);

        return found;
    }

    /// <summary>Reads the exports this account has, newest first.</summary>
    /// <param name="account">The account asked about.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The exports.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller does not hold the export permission.</exception>
    public Task<IReadOnlyList<MailboxExport>> ListAsync(MailAccountId account, CancellationToken cancellationToken)
    {
        this.authorization.RequirePermission(MailFathomPermission.AdminExport);

        return this.exports.ListAsync(account, ListingLimit, cancellationToken);
    }

    /// <summary>Stops an export that is still being written, and deletes whatever it had produced.</summary>
    /// <param name="account">The account the export belongs to.</param>
    /// <param name="export">The export's identity.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The export as it now stands.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller does not hold the export permission.</exception>
    /// <exception cref="MailboxExportRefusedException">Thrown when the account holds no export under that identity.</exception>
    /// <remarks>
    /// Cancelling one that already finished is not a failure and not a cancellation either: the archive is there and the
    /// act to take on it is a deletion, so the export is answered as it stands. The writing job reads the record at its
    /// checkpoint, once every hundred messages, so a cancellation reaches it within a hundred messages rather than at
    /// the end of the mailbox.
    /// </remarks>
    public async Task<MailboxExport> CancelAsync(
        MailAccountId account,
        MailboxExportId export,
        CancellationToken cancellationToken)
    {
        this.authorization.RequirePermission(MailFathomPermission.AdminExport);

        var found = await this.RequireExportAsync(account, export, cancellationToken);

        if (!found.IsInFlight)
        {
            return found;
        }

        var cancelled = found with
        {
            State = MailboxExportState.Cancelled,
            ObjectLocator = null,
            ExpiresAt = null,
        };

        if (!await this.exports.SaveAsync(cancelled, found.State, cancellationToken))
        {
            // It finished, failed, or was cancelled while this was being decided. Whatever it became is the answer.
            return await this.RequireExportAsync(account, export, cancellationToken);
        }

        await this.RemoveArchiveAsync(found, cancellationToken);

        await this.RecordAsync(
            MailboxExportActKind.Cancelled,
            account,
            found.FolderPath,
            found.Id,
            found.MessageCount,
            found.ByteCount,
            cancellationToken);

        return cancelled;
    }

    /// <summary>Deletes a finished export's archive before its retention period ends.</summary>
    /// <param name="account">The account the export belongs to.</param>
    /// <param name="export">The export's identity.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The export as it now stands.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller does not hold the export permission.</exception>
    /// <exception cref="MailboxExportRefusedException">Thrown when the account holds no export under that identity.</exception>
    /// <remarks>
    /// Deleting an archive that has already expired or been deleted is not an error, because the caller asked for a
    /// state the deployment is already in — which is exactly what an operator deleting after a download wants to be
    /// able to repeat. An export still being written is cancelled instead, since there is no archive to delete yet.
    /// </remarks>
    public async Task<MailboxExport> DeleteAsync(
        MailAccountId account,
        MailboxExportId export,
        CancellationToken cancellationToken)
    {
        this.authorization.RequirePermission(MailFathomPermission.AdminExport);

        var found = await this.RequireExportAsync(account, export, cancellationToken);

        if (found.IsInFlight)
        {
            return await this.CancelAsync(account, export, cancellationToken);
        }

        if (found.State is not MailboxExportState.Completed)
        {
            return found;
        }

        var deleted = found with
        {
            State = MailboxExportState.Deleted,
            ObjectLocator = null,
            ExpiresAt = null,
        };

        if (!await this.exports.SaveAsync(deleted, MailboxExportState.Completed, cancellationToken))
        {
            return await this.RequireExportAsync(account, export, cancellationToken);
        }

        await this.RemoveArchiveAsync(found, cancellationToken);

        await this.RecordAsync(
            MailboxExportActKind.Deleted,
            account,
            found.FolderPath,
            found.Id,
            found.MessageCount,
            found.ByteCount,
            cancellationToken);

        return deleted;
    }

    /// <summary>Opens a finished export's archive, so the transport can copy it to the caller.</summary>
    /// <param name="account">The account the export belongs to.</param>
    /// <param name="export">The export's identity.</param>
    /// <param name="cancellationToken">Cancels opening the archive.</param>
    /// <returns>The archive, which the caller disposes.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller does not hold the export permission.</exception>
    /// <exception cref="MailboxExportRefusedException">Thrown when the account holds no such export, or its archive is no longer there to serve.</exception>
    /// <remarks>
    /// A download already begun is not cut off by the retention period ending, because the stream is the store's own and
    /// the expiry deletes an object rather than a reader's connection.
    /// </remarks>
    public async Task<MailboxExportArchive> OpenArchiveAsync(
        MailAccountId account,
        MailboxExportId export,
        CancellationToken cancellationToken)
    {
        this.authorization.RequirePermission(MailFathomPermission.AdminExport);

        var found = await this.RequireExportAsync(account, export, cancellationToken);

        if (!found.IsDownloadable)
        {
            throw MailboxExportRefusedException.NoLongerDownloadable();
        }

        var content = await this.archives.OpenReadAsync(found.ObjectLocator!, cancellationToken)
            ?? throw MailboxExportRefusedException.NoLongerDownloadable();

        await this.RecordAsync(
            MailboxExportActKind.Downloaded,
            account,
            found.FolderPath,
            found.Id,
            found.MessageCount,
            found.ByteCount,
            cancellationToken);

        return new MailboxExportArchive(content, found.ArchiveByteLength, FileNameFor(found));
    }

    /// <summary>Names the file a downloaded archive is offered as, from nothing a folder or a message says.</summary>
    /// <param name="export">The export being downloaded.</param>
    /// <returns>A file name of ASCII letters, digits, and hyphens alone.</returns>
    /// <remarks>
    /// The export's own identity rather than the account's identifier or the folder's path: a file name travels into a
    /// downloads directory, a shell history, and a backup index, and neither of those two is something an export has any
    /// reason to write there.
    /// </remarks>
    public static string FileNameFor(MailboxExport export)
    {
        ArgumentNullException.ThrowIfNull(export);

        return $"mailfathom-export-{export.Id.Value:N}.zip";
    }

    private async Task<MailboxExport> RequireExportAsync(
        MailAccountId account,
        MailboxExportId export,
        CancellationToken cancellationToken) =>
        await this.exports.FindAsync(account, export, cancellationToken)
        ?? throw MailboxExportRefusedException.NotFound();

    private void RequireSomewhereToKeepAnArchive()
    {
        if (!this.archives.IsAvailable)
        {
            throw MailboxExportRefusedException.NoObjectStorage(ObjectStorageSettingName);
        }
    }

    private async Task RequireStorageHeadroomAsync(
        MailAccountId account,
        long byteCount,
        CancellationToken cancellationToken)
    {
        if (byteCount == 0)
        {
            return;
        }

        // The claim is taken and given straight back, which is what asks the ceiling a question it has no other member
        // for: a deployment that declared no ceiling grants without reaching the database at all, and one that did
        // answers against what every concurrent write is already reserving. Holding it for the export would be a
        // reservation outliving its own expiry, since a claim covers one message's fetch rather than a mailbox's walk.
        var attempt = await this.ceiling.TryClaimAsync(account, byteCount, cancellationToken);

        if (attempt.Claim is { } claim)
        {
            await claim.DisposeAsync();

            return;
        }

        throw MailboxExportRefusedException.NoStorageHeadroom(byteCount, attempt.ReachedBound);
    }

    private async Task RemoveArchiveAsync(MailboxExport export, CancellationToken cancellationToken)
    {
        if (export.ObjectLocator is { } locator)
        {
            await this.archives.DeleteAsync(locator, cancellationToken);
        }
    }

    private Task RecordAsync(
        MailboxExportActKind kind,
        MailAccountId account,
        string? folderPath,
        MailboxExportId? export,
        long messageCount,
        long byteCount,
        CancellationToken cancellationToken) =>
        this.auditor.RecordAsync(
            new MailboxExportAct(
                this.authorization.PrincipalIdentity ?? "unknown",
                MailFathomPermission.AdminExport,
                kind,
                account,
                folderPath,
                export,
                messageCount,
                byteCount,
                this.timeProvider.GetUtcNow()),
            cancellationToken);
}

/// <summary>What starting an export answered with: the figures it was decided on, and the export now recorded.</summary>
/// <param name="Measurement">What the export is expected to carry, measured before the work was queued.</param>
/// <param name="Export">The export.</param>
/// <param name="WasAlreadyRunning">Whether this is an export that was already being written rather than one this request started.</param>
public sealed record MailboxExportStart(
    MailboxExportMeasurement Measurement,
    MailboxExport Export,
    bool WasAlreadyRunning);

/// <summary>A finished archive opened for reading, which the caller disposes once it has been served.</summary>
/// <param name="Content">The archive's bytes.</param>
/// <param name="ByteLength">How long the archive is, where the record knows.</param>
/// <param name="FileName">The name the archive is offered under.</param>
public sealed record MailboxExportArchive(Stream Content, long? ByteLength, string FileName) : IAsyncDisposable
{
    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await this.Content.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}
