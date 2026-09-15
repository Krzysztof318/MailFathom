// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.IO.Compression;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Jobs;
using MailFathom.Application.Jobs.Payloads;
using MailFathom.Application.Mail.Export;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Exports;
using MailFathom.Domain.Failures;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Mail.Export;

public sealed class MailboxExportHandlerTests
{
    private static readonly MailAccountId Account = SyntheticMailAccount.Deployment;

    private static readonly DateTimeOffset Now = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);

    private static readonly MailboxExportFolder Inbox = new("INBOX", ["INBOX"], IsInbox: true);

    [Fact]
    public async Task RunAsync_AQueuedExport_WritesTheArchiveAndRecordsWhereItIsAndWhenItGoes()
    {
        // Arrange
        var queued = Queued();
        var store = new InMemoryMailboxExportStore().Holding(queued);
        var archives = new InMemoryMailboxExportArchiveStore();
        var auditor = new RecordingMailboxExportAuditor();
        var handler = HandlerOver(
            store,
            ReaderHolding(MessageOf(1, byteLength: 12), MessageOf(2, byteLength: 12)),
            archives,
            auditor: auditor);

        // Act
        await handler.RunAsync(PayloadFor(queued), TestContext.Current.CancellationToken);

        // Assert
        var written = store.Find(queued.Id);
        Assert.NotNull(written);
        Assert.Equal(MailboxExportState.Completed, written.State);
        Assert.Equal(2, written.MessageCount);
        Assert.Equal(24, written.ByteCount);
        Assert.Equal(Now, written.CompletedAt);
        Assert.Equal(Now + MailboxExportSettings.DefaultRetention, written.ExpiresAt);
        Assert.NotNull(written.ObjectLocator);
        Assert.NotNull(archives.Read(written.ObjectLocator));
        Assert.Contains(auditor.Acts, act => act.Kind == MailboxExportActKind.Completed);
    }

    [Fact]
    public async Task RunAsync_AQueuedExport_WritesOneEntryPerMessageBesideTheMaildirItBelongsIn()
    {
        // Arrange
        var queued = Queued();
        var store = new InMemoryMailboxExportStore().Holding(queued);
        var archives = new InMemoryMailboxExportArchiveStore();
        var handler = HandlerOver(store, ReaderHolding(MessageOf(1, byteLength: 12)), archives);

        // Act
        await handler.RunAsync(PayloadFor(queued), TestContext.Current.CancellationToken);

        // Assert
        var locator = store.Find(queued.Id)?.ObjectLocator;
        Assert.NotNull(locator);

        using var archive = new ZipArchive(
            new MemoryStream(archives.Read(locator)!, writable: false),
            ZipArchiveMode.Read);

        Assert.Equal(
            ["cur/", "keywords.json", "new/", "tmp/"],
            archive.Entries
                .Select(entry => entry.FullName)
                .Where(name => name.EndsWith('/') || !name.Contains('/', StringComparison.Ordinal))
                .Order(StringComparer.Ordinal));

        Assert.Single(
            archive.Entries,
            entry => entry.FullName.StartsWith("cur/", StringComparison.Ordinal) && !entry.FullName.EndsWith('/'));
    }

    /// <summary>At least-once execution is what makes a second attempt reachable, so one is a no-op rather than a failure.</summary>
    [Fact]
    public async Task RunAsync_AnExportThatIsNoLongerQueued_WritesNothingAtAll()
    {
        // Arrange
        var finished = Queued() with { State = MailboxExportState.Completed };
        var store = new InMemoryMailboxExportStore().Holding(finished);
        var archives = new InMemoryMailboxExportArchiveStore();
        var handler = HandlerOver(store, ReaderHolding(MessageOf(1, byteLength: 12)), archives);

        // Act
        await handler.RunAsync(PayloadFor(finished), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailboxExportState.Completed, store.Find(finished.Id)?.State);
        Assert.Equal(0, archives.AbandonedWrites);
    }

    [Fact]
    public async Task RunAsync_AnExportRecordAnErasureRemoved_WritesNothingAtAll()
    {
        // Arrange
        var store = new InMemoryMailboxExportStore();
        var archives = new InMemoryMailboxExportArchiveStore();
        var handler = HandlerOver(store, ReaderHolding(MessageOf(1, byteLength: 12)), archives);

        // Act
        await handler.RunAsync(PayloadFor(Queued()), TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(archives.Deleted);
        Assert.Equal(0, archives.AbandonedWrites);
    }

    /// <summary>A run that stops leaves nothing to resume into, so the export goes back to queued and is written again from the start.</summary>
    [Fact]
    public async Task RunAsync_TheRunCancelled_ReturnsTheExportToQueuedAndLeavesNoArchiveBehind()
    {
        // Arrange
        var queued = Queued();
        var store = new InMemoryMailboxExportStore().Holding(queued);
        var archives = new InMemoryMailboxExportArchiveStore();
        var handler = HandlerOver(store, ReaderHolding(MessageOf(1, byteLength: 12)), archives);

        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        // Act
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            handler.RunAsync(PayloadFor(queued), cancellation.Token));

        // Assert
        Assert.Equal(MailboxExportState.Queued, store.Find(queued.Id)?.State);
        Assert.Equal(1, archives.AbandonedWrites);
        Assert.Empty(archives.Read($"mailbox-exports/{queued.Id.Value}") ?? []);
    }

    /// <summary>The limit is measured again while the archive is written, because a mailbox grows between the measurement and the job.</summary>
    [Fact]
    public async Task RunAsync_TheMailboxPastTheSizeLimitWhileItIsWritten_FailsWithTheRefusalsOwnCodeAndNoArchive()
    {
        // Arrange
        var queued = Queued();
        var store = new InMemoryMailboxExportStore().Holding(queued);
        var archives = new InMemoryMailboxExportArchiveStore();
        var handler = HandlerOver(
            store,
            ReaderHolding([.. Enumerable.Range(1, 100).Select(ordinal => MessageOf(ordinal, byteLength: 32))]),
            archives,
            settings: new MailboxExportSettings(1024, MailboxExportSettings.DefaultRetention));

        // Act
        var refusal = await Assert.ThrowsAsync<MailboxExportRefusedException>(() =>
            handler.RunAsync(PayloadFor(queued), TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomErrorCode.MailboxExportTooLarge, refusal.ErrorCode);

        var failed = store.Find(queued.Id);
        Assert.Equal(MailboxExportState.Failed, failed?.State);
        Assert.Equal(MailFathomErrorCode.MailboxExportTooLarge, failed?.FailureCode);
        Assert.Null(failed?.ObjectLocator);
        Assert.Equal(1, archives.AbandonedWrites);
    }

    /// <summary>The limit stops the message that would cross it, rather than whichever message the next checkpoint falls on.</summary>
    /// <remarks>
    /// A mailbox of fewer messages than the checkpoint interval reaches no checkpoint at all, so a bound asked only
    /// there would admit the whole of it however far past the limit it went — and would have sent it to the endpoint
    /// before saying so. The figures here are deliberately small enough that nothing else would have stopped it.
    /// </remarks>
    [Fact]
    public async Task RunAsync_AMailboxPastTheLimitBeforeAnyCheckpoint_IsRefusedAtTheMessageThatWouldCrossIt()
    {
        // Arrange
        var queued = Queued();
        var store = new InMemoryMailboxExportStore().Holding(queued);
        var archives = new InMemoryMailboxExportArchiveStore();
        var handler = HandlerOver(
            store,
            ReaderHolding([.. Enumerable.Range(1, 5).Select(ordinal => MessageOf(ordinal, byteLength: 12))]),
            archives,
            settings: new MailboxExportSettings(30, MailboxExportSettings.DefaultRetention));

        // Act
        var refusal = await Assert.ThrowsAsync<MailboxExportRefusedException>(() =>
            handler.RunAsync(PayloadFor(queued), TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomErrorCode.MailboxExportTooLarge, refusal.ErrorCode);

        var failed = store.Find(queued.Id);
        Assert.Equal(MailboxExportState.Failed, failed?.State);
        Assert.Equal(MailFathomErrorCode.MailboxExportTooLarge, failed?.FailureCode);
        Assert.Equal(1, archives.AbandonedWrites);
    }

    /// <summary>A payload erased between the walk and the read is gone rather than left out of an archive that holds it.</summary>
    [Fact]
    public async Task RunAsync_AMessageWhosePayloadIsNoLongerHeld_LeavesItOutAndCountsNeitherItNorItsBytes()
    {
        // Arrange
        var queued = Queued();
        var store = new InMemoryMailboxExportStore().Holding(queued);
        var held = MessageOf(1, byteLength: 12);
        var erased = MessageOf(2, byteLength: 12);
        var handler = HandlerOver(
            store,
            ReaderHolding(held, erased),
            new InMemoryMailboxExportArchiveStore(),
            content: ContentHolding(held));

        // Act
        await handler.RunAsync(PayloadFor(queued), TestContext.Current.CancellationToken);

        // Assert
        var written = store.Find(queued.Id);
        Assert.Equal(1, written?.MessageCount);
        Assert.Equal(12, written?.ByteCount);
    }

    [Fact]
    public async Task RunAsync_APayloadOfAnotherJobType_IsRefusedAsTheWrongContract()
    {
        // Arrange
        var handler = HandlerOver(
            new InMemoryMailboxExportStore(),
            ReaderHolding(),
            new InMemoryMailboxExportArchiveStore());

        // Act, Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            handler.RunAsync(
                new StatedPayload(),
                TestContext.Current.CancellationToken));
    }

    private static MailboxExport Queued() => MailboxExport.Queued(
        MailboxExportId.Create(Guid.Parse("0199a0c0-0000-7000-8000-0000000000bb")),
        Account,
        folderPath: null,
        Now);

    private static ExportMailboxJobPayload PayloadFor(MailboxExport export) =>
        ExportMailboxJobPayload.For(export.Account, export.Id);

    private static ExportableMessage MessageOf(int ordinal, long byteLength) => new(
        StoredEmailId.Create(Guid.Parse($"0199a0c0-0000-7000-8000-{ordinal:D12}")),
        Inbox,
        Now,
        byteLength,
        MaildirFlagSet.Seen,
        []);

    private static StatedMailboxExportReader ReaderHolding(params ExportableMessage[] messages)
    {
        var reader = new StatedMailboxExportReader().Holding(Inbox);

        foreach (var message in messages)
        {
            reader.Holding(message);
        }

        return reader;
    }

    private static IEmailContentStore ContentHolding(params ExportableMessage[] messages)
    {
        var content = Substitute.For<IEmailContentStore>();

        content.FindStoredContentAsync(Arg.Any<StoredEmailId>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<StoredEmailContent?>(null));

        foreach (var message in messages)
        {
            var payload = new byte[message.ByteLength];

            content.FindStoredContentAsync(message.Message, Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<StoredEmailContent?>(
                    new StoredEmailContent(payload, payload.Length, ReadOnlyMemory<byte>.Empty)));
        }

        return content;
    }

    private static MailboxExportHandler HandlerOver(
        InMemoryMailboxExportStore store,
        StatedMailboxExportReader reader,
        InMemoryMailboxExportArchiveStore archives,
        IEmailContentStore? content = null,
        RecordingMailboxExportAuditor? auditor = null,
        MailboxExportSettings? settings = null) =>
        new(
            store,
            reader,
            archives,
            content ?? AnsweringEveryPayload(),
            auditor ?? new RecordingMailboxExportAuditor(),
            settings ?? MailboxExportSettings.Default,
            new FakeTimeProvider(Now));

    private static IEmailContentStore AnsweringEveryPayload()
    {
        var content = Substitute.For<IEmailContentStore>();

        content.FindStoredContentAsync(Arg.Any<StoredEmailId>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                var payload = new byte[12];

                return Task.FromResult<StoredEmailContent?>(
                    new StoredEmailContent(payload, payload.Length, ReadOnlyMemory<byte>.Empty));
            });

        return content;
    }

    /// <summary>A payload of the wrong contract, which is what the handler's refusal is about.</summary>
    private sealed record StatedPayload : IJobPayload
    {
        public JobType JobType => JobType.ReclaimContentObjects;
    }
}
