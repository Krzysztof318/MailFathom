// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Jobs;
using MailFathom.Application.Mail.Export;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Exports;
using MailFathom.Domain.Failures;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Mail.Export;

public sealed class MailboxExportsTests
{
    private const string WrittenLocator = "mailbox-exports/written";

    private static readonly MailAccountId Account = SyntheticMailAccount.Deployment;

    private static readonly DateTimeOffset Now = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);

    private static readonly MailboxExportFolder Inbox = new("INBOX", ["INBOX"], IsInbox: true);

    [Fact]
    public async Task MeasureAsync_AMailbox_SumsTheRecordedLengthsAndStartsNothing()
    {
        // Arrange
        var jobs = Substitute.For<IJobStore>();
        var reader = new StatedMailboxExportReader()
            .Holding(MessageOf(1, byteLength: 400))
            .Holding(MessageOf(2, byteLength: 600));
        var exports = ExportsOver(reader: reader, jobs: jobs);

        // Act
        var measurement = await exports.MeasureAsync(Account, folderPath: null, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, measurement.MessageCount);
        Assert.Equal(1000, measurement.ByteCount);
        await jobs.DidNotReceive().EnqueueAsync(Arg.Any<JobEnqueueRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MeasureAsync_ADeploymentWithNoObjectBackend_RefusesNamingTheSettingThatWouldTurnItOn()
    {
        // Arrange
        var exports = ExportsOver(archives: new InMemoryMailboxExportArchiveStore(isAvailable: false));

        // Act
        var refusal = await Assert.ThrowsAsync<MailboxExportRefusedException>(() =>
            exports.MeasureAsync(Account, folderPath: null, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomErrorCode.MailboxExportUnavailable, refusal.ErrorCode);
        Assert.Contains(MailboxExports.ObjectStorageSettingName, refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MeasureAsync_ACallerWithoutTheExportGrant_IsRefusedBeforeAnythingIsRead()
    {
        // Arrange
        var reader = new StatedMailboxExportReader();
        var exports = ExportsOver(
            authorization: AccessAuthorizations.ForAdministratorGranted(MailFathomPermission.AdminRead),
            reader: reader);

        // Act
        await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() =>
            exports.MeasureAsync(Account, folderPath: null, TestContext.Current.CancellationToken));

        // Assert
        Assert.Empty(reader.AskedFolderPaths);
    }

    [Fact]
    public async Task StartAsync_AMeasuredMailbox_RecordsAQueuedExportAndQueuesTheWork()
    {
        // Arrange
        var store = new InMemoryMailboxExportStore();
        var auditor = new RecordingMailboxExportAuditor();
        var exports = ExportsOver(
            store: store,
            reader: new StatedMailboxExportReader().Holding(MessageOf(1, byteLength: 400)),
            auditor: auditor,
            jobs: AcceptingJobs());

        // Act
        var started = await exports.StartAsync(Account, folderPath: null, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(started.WasAlreadyRunning);
        Assert.Equal(MailboxExportState.Queued, started.Export.State);
        Assert.Equal(Now, started.Export.RequestedAt);
        Assert.Equal(400, started.Measurement.ByteCount);
        Assert.Equal(MailboxExportState.Queued, store.Find(started.Export.Id)?.State);
        Assert.Contains(auditor.Acts, act => act.Kind == MailboxExportActKind.Started);
    }

    [Fact]
    public async Task StartAsync_TheSameScopeAlreadyBeingWritten_AnswersWithThatExportRatherThanStartingItOver()
    {
        // Arrange
        var inFlight = MailboxExport.Queued(NewExportId(), Account, folderPath: null, Now);
        var jobs = Substitute.For<IJobStore>();
        var exports = ExportsOver(store: new InMemoryMailboxExportStore().Holding(inFlight), jobs: jobs);

        // Act
        var started = await exports.StartAsync(Account, folderPath: null, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(started.WasAlreadyRunning);
        Assert.Equal(inFlight.Id, started.Export.Id);
        await jobs.DidNotReceive().EnqueueAsync(Arg.Any<JobEnqueueRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartAsync_ADifferentScopeAlreadyBeingWritten_IsRefusedSoAnAccountWritesOneArchiveAtATime()
    {
        // Arrange
        var inFlight = MailboxExport.Queued(NewExportId(), Account, "Archive", Now);
        var exports = ExportsOver(store: new InMemoryMailboxExportStore().Holding(inFlight));

        // Act
        var refusal = await Assert.ThrowsAsync<MailboxExportRefusedException>(() =>
            exports.StartAsync(Account, folderPath: null, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomErrorCode.MailboxExportAlreadyRunning, refusal.ErrorCode);
    }

    [Fact]
    public async Task StartAsync_AMeasurementPastTheConfiguredLimit_IsRefusedBeforeAnyJobExists()
    {
        // Arrange
        var store = new InMemoryMailboxExportStore();
        var jobs = Substitute.For<IJobStore>();
        var exports = ExportsOver(
            store: store,
            reader: new StatedMailboxExportReader().Holding(MessageOf(1, byteLength: 4096)),
            jobs: jobs,
            settings: new MailboxExportSettings(1024, MailboxExportSettings.DefaultRetention));

        // Act
        var refusal = await Assert.ThrowsAsync<MailboxExportRefusedException>(() =>
            exports.StartAsync(Account, folderPath: null, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomErrorCode.MailboxExportTooLarge, refusal.ErrorCode);
        Assert.Empty(await store.ListAsync(Account, 50, TestContext.Current.CancellationToken));
        await jobs.DidNotReceive().EnqueueAsync(Arg.Any<JobEnqueueRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartAsync_NoHeadroomUnderTheStorageCeiling_IsRefusedBeforeAnyJobExists()
    {
        // Arrange
        var store = new InMemoryMailboxExportStore();
        var jobs = Substitute.For<IJobStore>();
        var exports = ExportsOver(
            store: store,
            reader: new StatedMailboxExportReader().Holding(MessageOf(1, byteLength: 500)),
            jobs: jobs,
            ceiling: new StoredContentCeiling(
                new InMemoryStoredContentClaimStore().HoldingInTotal(900),
                ceilingBytes: 1000));

        // Act
        var refusal = await Assert.ThrowsAsync<MailboxExportRefusedException>(() =>
            exports.StartAsync(Account, folderPath: null, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomErrorCode.MailboxExportTooLarge, refusal.ErrorCode);
        Assert.Empty(await store.ListAsync(Account, 50, TestContext.Current.CancellationToken));
        await jobs.DidNotReceive().EnqueueAsync(Arg.Any<JobEnqueueRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReadAsync_AnExportTheAccountDoesNotHold_IsRefusedAsNotFound()
    {
        // Arrange
        var exports = ExportsOver();

        // Act
        var refusal = await Assert.ThrowsAsync<MailboxExportRefusedException>(() =>
            exports.ReadAsync(Account, NewExportId(), TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomErrorCode.MailboxExportNotFound, refusal.ErrorCode);
    }

    [Fact]
    public async Task ReadAsync_AnExportOfAnotherAccount_IsRefusedAsNotFound()
    {
        // Arrange
        var other = MailboxExport.Queued(
            NewExportId(),
            MailAccountId.Create("another-account"),
            folderPath: null,
            Now);

        var exports = ExportsOver(store: new InMemoryMailboxExportStore().Holding(other));

        // Act
        var refusal = await Assert.ThrowsAsync<MailboxExportRefusedException>(() =>
            exports.ReadAsync(Account, other.Id, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomErrorCode.MailboxExportNotFound, refusal.ErrorCode);
    }

    [Fact]
    public async Task CancelAsync_AnExportBeingWritten_RecordsTheCancellationAndDeletesWhatItHadWritten()
    {
        // Arrange
        var running = Completed() with
        {
            State = MailboxExportState.Running,
            CompletedAt = null,
            ExpiresAt = null,
        };

        var archives = new InMemoryMailboxExportArchiveStore();
        var exports = ExportsOver(store: new InMemoryMailboxExportStore().Holding(running), archives: archives);

        // Act
        var cancelled = await exports.CancelAsync(Account, running.Id, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailboxExportState.Cancelled, cancelled.State);
        Assert.Null(cancelled.ObjectLocator);
        Assert.Contains(WrittenLocator, archives.Deleted, StringComparer.Ordinal);
    }

    [Fact]
    public async Task CancelAsync_AnExportThatHasAlreadyFinished_AnswersWithItRatherThanDeletingTheArchive()
    {
        // Arrange
        var finished = Completed();
        var archives = new InMemoryMailboxExportArchiveStore();
        var exports = ExportsOver(store: new InMemoryMailboxExportStore().Holding(finished), archives: archives);

        // Act
        var answered = await exports.CancelAsync(Account, finished.Id, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailboxExportState.Completed, answered.State);
        Assert.Empty(archives.Deleted);
    }

    [Fact]
    public async Task DeleteAsync_AFinishedArchive_RemovesItAndLeavesTheExportNoLongerDownloadable()
    {
        // Arrange
        var finished = Completed();
        var archives = new InMemoryMailboxExportArchiveStore();
        var exports = ExportsOver(store: new InMemoryMailboxExportStore().Holding(finished), archives: archives);

        // Act
        var deleted = await exports.DeleteAsync(Account, finished.Id, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailboxExportState.Deleted, deleted.State);
        Assert.Null(deleted.ObjectLocator);
        Assert.Contains(WrittenLocator, archives.Deleted, StringComparer.Ordinal);

        var refusal = await Assert.ThrowsAsync<MailboxExportRefusedException>(() =>
            exports.OpenArchiveAsync(Account, finished.Id, TestContext.Current.CancellationToken));

        Assert.Equal(MailFathomErrorCode.MailboxExportNoLongerDownloadable, refusal.ErrorCode);
    }

    [Fact]
    public async Task DeleteAsync_AnArchiveThatIsAlreadyGone_SucceedsSoACleanupCanBeRepeated()
    {
        // Arrange
        var expired = Completed() with { State = MailboxExportState.Expired, ObjectLocator = null };
        var exports = ExportsOver(store: new InMemoryMailboxExportStore().Holding(expired));

        // Act
        var answered = await exports.DeleteAsync(Account, expired.Id, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailboxExportState.Expired, answered.State);
    }

    [Fact]
    public async Task OpenArchiveAsync_AFinishedExport_ServesTheArchiveUnderAFileNameCarryingNothingButItsIdentity()
    {
        // Arrange
        var finished = Completed();
        var archives = new InMemoryMailboxExportArchiveStore();
        var located = finished with { ObjectLocator = await ArchiveOf(archives, finished.Id) };
        var auditor = new RecordingMailboxExportAuditor();
        var exports = ExportsOver(
            store: new InMemoryMailboxExportStore().Holding(located),
            archives: archives,
            auditor: auditor);

        // Act
        await using var archive = await exports.OpenArchiveAsync(
            Account,
            located.Id,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal($"mailfathom-export-{located.Id.Value:N}.zip", archive.FileName);
        Assert.DoesNotContain(Account.Value, archive.FileName, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(auditor.Acts, act => act.Kind == MailboxExportActKind.Downloaded);
    }

    [Fact]
    public async Task ListAsync_AnAccountWithSeveralExports_ReportsThemNewestFirst()
    {
        // Arrange
        var older = MailboxExport.Queued(NewExportId(), Account, folderPath: null, Now.AddHours(-2));
        var newer = MailboxExport.Queued(NewExportId(), Account, "Archive", Now);
        var exports = ExportsOver(store: new InMemoryMailboxExportStore().Holding(older).Holding(newer));

        // Act
        var listing = await exports.ListAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([newer.Id, older.Id], listing.Select(export => export.Id));
    }

    private static async Task<string> ArchiveOf(InMemoryMailboxExportArchiveStore archives, MailboxExportId exportId)
    {
        await using var write = await archives.BeginWriteAsync(exportId, TestContext.Current.CancellationToken);

        await write.Content.WriteAsync(new byte[] { 1, 2, 3 }, TestContext.Current.CancellationToken);
        await write.CompleteAsync(TestContext.Current.CancellationToken);

        return write.ObjectLocator;
    }

    private static MailboxExportId NewExportId() => MailboxExportId.Create(Guid.CreateVersion7());

    private static IJobStore AcceptingJobs()
    {
        var jobs = Substitute.For<IJobStore>();

        jobs.EnqueueAsync(Arg.Any<JobEnqueueRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(JobEnqueueResult.Created(JobId.Create(Guid.CreateVersion7()))));

        return jobs;
    }

    private static ExportableMessage MessageOf(int ordinal, long byteLength) => new(
        StoredEmailId.Create(Guid.Parse($"0199a0c0-0000-7000-8000-00000000000{ordinal}")),
        Inbox,
        Now,
        byteLength,
        MaildirFlagSet.Seen,
        []);

    private static MailboxExport Completed() => new(
        MailboxExportId.Create(Guid.Parse("0199a0c0-0000-7000-8000-0000000000aa")),
        Account,
        FolderPath: null,
        MailboxExportState.Completed,
        Now,
        MessageCount: 2,
        ByteCount: 1000,
        ArchiveByteLength: 900,
        ObjectLocator: WrittenLocator,
        CompletedAt: Now,
        ExpiresAt: Now.AddHours(48),
        FailureCode: null);

    private static MailboxExports ExportsOver(
        AccessAuthorization? authorization = null,
        InMemoryMailboxExportStore? store = null,
        StatedMailboxExportReader? reader = null,
        InMemoryMailboxExportArchiveStore? archives = null,
        RecordingMailboxExportAuditor? auditor = null,
        IJobStore? jobs = null,
        StoredContentCeiling? ceiling = null,
        MailboxExportSettings? settings = null) =>
        new(
            authorization ?? AccessAuthorizations.ForAdministratorGranted(MailFathomPermission.AdminExport),
            store ?? new InMemoryMailboxExportStore(),
            reader ?? new StatedMailboxExportReader(),
            archives ?? new InMemoryMailboxExportArchiveStore(),
            auditor ?? new RecordingMailboxExportAuditor(),
            jobs ?? Substitute.For<IJobStore>(),
            ceiling ?? new StoredContentCeiling(new InMemoryStoredContentClaimStore(), ceilingBytes: null),
            settings ?? MailboxExportSettings.Default,
            new FakeTimeProvider(Now));
}
