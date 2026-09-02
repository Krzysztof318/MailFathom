// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Security.Cryptography;
using MailFathom.Application.Access;
using MailFathom.Application.EmailContent.Move;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Persistence;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Access;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.EmailContent.Move;

/// <summary>Covers the walk that carries payloads out of the database and into the bucket.</summary>
/// <remarks>
/// What makes it usable against a live deployment is that a pass ends on its own, commits where it got to, and refuses
/// to repoint anything it could not verify. Every test here is about one of those three.
/// </remarks>
public sealed class StoredContentMoveTests
{
    private static readonly DateTimeOffset Moment = new(2026, 8, 24, 12, 0, 0, TimeSpan.Zero);

    private readonly InMemoryStoredContentMoveRunStore runs = new();
    private readonly InMemoryStoredContentMoveStore content = new();
    private readonly InMemoryEmailContentObjectBackend objects = new();
    private readonly RecordingStoredContentMoveTelemetry telemetry = new();

    /// <summary>A deployment holding less than one pass carries has its content moved and the move ended.</summary>
    [Fact]
    public async Task RunAsync_FewerPayloadsThanOnePassCarries_MovesThemAndEndsTheWalk()
    {
        // Arrange
        this.ArrangeRunningMove();
        this.ArrangePayload(EmailContentKind.IncomingMessage, 1);
        this.ArrangePayload(EmailContentKind.OutgoingMessage, 2);
        this.ArrangePayload(EmailContentKind.RecurringSendDraft, 3);
        this.ArrangePayload(EmailContentKind.MailDraft, 4);

        // Act
        var pass = await this.MoveOver().RunAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(4L, pass.CopiedPayloadCount);
        Assert.Equal(0L, pass.FailedPayloadCount);
        Assert.False(pass.PayloadsRemain);
        Assert.Equal(4, this.content.Repoints.Count);
        Assert.True(this.telemetry.ReachedEndOfContent);
        Assert.Equal(StoredContentMoveState.Completed, this.runs.Current?.State);
        Assert.Equal(Moment, this.runs.Current?.EndedAt);
    }

    /// <summary>Each row points at the object that was written for it, once the object has been read back and checked.</summary>
    [Fact]
    public async Task RunAsync_PayloadCarried_RepointsTheRowAtTheVerifiedObject()
    {
        // Arrange
        this.ArrangeRunningMove();
        this.ArrangePayload(EmailContentKind.IncomingMessage, 1);

        // Act
        await this.MoveOver().RunAsync(TestContext.Current.CancellationToken);

        // Assert
        var repoint = Assert.Single(this.content.Repoints);
        Assert.Equal(EmailContentKind.IncomingMessage, repoint.Kind);
        Assert.Contains(repoint.ObjectLocator, this.objects.Keys);

        // The instant the object was vouched for is written on the row rather than inferred later, because it is what
        // the safety interval holds the release back from, and nothing else on the row records when the copy happened.
        Assert.Equal(Moment, repoint.VerifiedAt);
    }

    /// <summary>One pass is bounded by its payload count, so a mailbox is many passes rather than one that never ends.</summary>
    [Fact]
    public async Task RunAsync_MorePayloadsThanOnePassCarries_StopsAndReportsRemainingWork()
    {
        // Arrange
        this.ArrangeRunningMove();
        this.ArrangePayloads(EmailContentKind.IncomingMessage, count: 5);

        // Act
        var pass = await this.MoveOver(payloadsPerPass: 3).RunAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(3L, pass.CopiedPayloadCount);
        Assert.True(pass.PayloadsRemain);
        Assert.False(this.telemetry.ReachedEndOfContent);
        Assert.Equal(StoredContentMoveState.Running, this.runs.Current?.State);
    }

    /// <summary>A pass ends on its byte ceiling as well, because twenty rows of video and twenty of notifications are not the same work.</summary>
    [Fact]
    public async Task RunAsync_PayloadsPassingTheByteCeiling_StopsOnItRatherThanOnTheCount()
    {
        // Arrange
        this.ArrangeRunningMove();
        this.ArrangePayloads(EmailContentKind.IncomingMessage, count: 5, byteLength: 400);

        // Act
        var pass = await this.MoveOver(maxBytesPerPass: 1000).RunAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(3L, pass.CopiedPayloadCount);
        Assert.Equal(1200L, pass.MovedByteCount);
        Assert.True(pass.PayloadsRemain);
    }

    /// <summary>The next pass resumes where the last one stopped rather than walking what it already carried.</summary>
    [Fact]
    public async Task RunAsync_AfterABoundedPass_RecordsThePositionTheNextPassResumesFrom()
    {
        // Arrange
        this.ArrangeRunningMove();
        this.ArrangePayloads(EmailContentKind.IncomingMessage, count: 4);

        // Act
        await this.MoveOver(payloadsPerPass: 2).RunAsync(TestContext.Current.CancellationToken);
        var second = await this.MoveOver(payloadsPerPass: 2).RunAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2L, second.CopiedPayloadCount);
        Assert.Equal(4L, this.runs.Current?.CopiedPayloadCount);
        Assert.Equal(4, this.content.Repoints.Select(repoint => repoint.PayloadId).Distinct().Count());
    }

    /// <summary>A payload whose stored bytes disagree with its own row is left alone, and nothing is written for it.</summary>
    [Fact]
    public async Task RunAsync_StoredPayloadDisagreesWithItsRow_WritesNoObjectAndCountsTheRefusal()
    {
        // Arrange
        this.ArrangeRunningMove();
        this.content.Arrange(
            EmailContentKind.IncomingMessage,
            PayloadId(1),
            [1, 2, 3],
            recordedSha256Hash: SHA256.HashData([9, 9, 9]));

        // Act
        var pass = await this.MoveOver().RunAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0L, pass.CopiedPayloadCount);
        Assert.Equal(1L, pass.FailedPayloadCount);
        Assert.Equal(0, this.objects.PlacementCount);
        Assert.Empty(this.content.Repoints);
        Assert.Equal(StoredContentMoveFailure.SourceMismatch, Assert.Single(this.telemetry.Failures));
    }

    /// <summary>An object that comes back as something else leaves the row database-backed, so the message stays readable.</summary>
    [Fact]
    public async Task RunAsync_ObjectComesBackDifferent_LeavesTheRowInTheDatabaseAndCountsTheRefusal()
    {
        // Arrange
        this.ArrangeRunningMove();
        this.ArrangePayload(EmailContentKind.IncomingMessage, 1);
        this.objects.CorruptedReadBack = [7, 7, 7, 7];

        // Act
        var pass = await this.MoveOver().RunAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0L, pass.CopiedPayloadCount);
        Assert.Equal(1L, pass.FailedPayloadCount);
        Assert.Empty(this.content.Repoints);
        Assert.Equal(StoredContentMoveFailure.ObjectMismatch, Assert.Single(this.telemetry.Failures));
    }

    /// <summary>An endpoint that answers and holds nothing under the key it was just given is a refusal rather than a repoint.</summary>
    [Fact]
    public async Task RunAsync_ObjectAbsentAfterTheWrite_LeavesTheRowInTheDatabaseAndCountsTheRefusal()
    {
        // Arrange
        this.ArrangeRunningMove();
        this.ArrangePayload(EmailContentKind.IncomingMessage, 1);
        this.objects.KeepsWhatItIsGiven = false;

        // Act
        var pass = await this.MoveOver().RunAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1L, pass.FailedPayloadCount);
        Assert.Empty(this.content.Repoints);
        Assert.Equal(StoredContentMoveFailure.ObjectAbsent, Assert.Single(this.telemetry.Failures));
    }

    /// <summary>A payload the process could never hold within its own budget is refused rather than waited on forever.</summary>
    [Fact]
    public async Task RunAsync_PayloadLargerThanTheWholeMemoryBudget_LeavesTheRowAndCountsTheRefusal()
    {
        // Arrange
        this.ArrangeRunningMove();
        this.ArrangePayloads(EmailContentKind.IncomingMessage, count: 1, byteLength: 64);

        // Act
        var pass = await this.MoveOver(memoryBudgetBytes: 32).RunAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1L, pass.FailedPayloadCount);
        Assert.Equal(0, this.objects.PlacementCount);
        Assert.Equal(StoredContentMoveFailure.Oversized, Assert.Single(this.telemetry.Failures));
    }

    /// <summary>A row a concurrent write repointed or replaced under the pass is not counted as one this move carried.</summary>
    [Fact]
    public async Task RunAsync_RowNoLongerDatabaseBackedWhenRepointed_DoesNotCountItAsMoved()
    {
        // Arrange
        this.ArrangeRunningMove();
        this.ArrangePayload(EmailContentKind.IncomingMessage, 1);
        this.content.RepointSucceeds = false;

        // Act
        var pass = await this.MoveOver().RunAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0L, pass.CopiedPayloadCount);
        Assert.Equal(0L, pass.FailedPayloadCount);
        Assert.Empty(this.telemetry.CopiedByteLengths);
    }

    /// <summary>A row whose payload is gone by the time the pass reads it is stepped past rather than counted either way.</summary>
    [Fact]
    public async Task RunAsync_PayloadGoneBetweenTheBatchAndTheRead_StepsPastItWithoutCountingIt()
    {
        // Arrange
        this.ArrangeRunningMove();
        this.content.Arrange(EmailContentKind.IncomingMessage, PayloadId(1), rawMime: null, recordedByteLength: 4);

        // Act
        var pass = await this.MoveOver().RunAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0L, pass.CopiedPayloadCount);
        Assert.Equal(0L, pass.FailedPayloadCount);
        Assert.Empty(this.telemetry.Failures);
        Assert.Equal(0, this.objects.PlacementCount);
    }

    /// <summary>A move an operator stopped carries nothing, which is what makes pausing immediate without cancelling anything.</summary>
    [Fact]
    public async Task RunAsync_MovePaused_CarriesNothingAndOpensNoPass()
    {
        // Arrange
        this.ArrangeRunningMove(StoredContentMoveState.Paused);
        this.ArrangePayload(EmailContentKind.IncomingMessage, 1);

        // Act
        var pass = await this.MoveOver().RunAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0L, pass.CopiedPayloadCount);
        Assert.False(pass.PayloadsRemain);
        Assert.Equal(0, this.telemetry.PassCount);
        Assert.Empty(this.content.Repoints);
    }

    /// <summary>A deployment nobody asked for a move carries nothing, which is what its worker's tick costs.</summary>
    [Fact]
    public async Task RunAsync_NoMoveAsked_CarriesNothing()
    {
        // Arrange
        this.ArrangePayload(EmailContentKind.IncomingMessage, 1);

        // Act
        var pass = await this.MoveOver().RunAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0L, pass.CopiedPayloadCount);
        Assert.Empty(this.content.Repoints);
        Assert.Equal(0, this.telemetry.PassCount);
    }

    /// <summary>An operator pausing while a pass runs keeps their decision, and the pass still records what it carried.</summary>
    [Fact]
    public async Task RunAsync_PausedWhileThePassRan_KeepsThePauseAndRecordsTheCounts()
    {
        // Arrange
        this.ArrangeRunningMove();
        this.ArrangePayloads(EmailContentKind.IncomingMessage, count: 2);
        this.runs.ArrangeDecisionOnRead(2, run => run with { State = StoredContentMoveState.Paused });

        // Act
        var pass = await this.MoveOver(payloadsPerPass: 1).RunAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1L, pass.CopiedPayloadCount);
        Assert.Equal(StoredContentMoveState.Paused, this.runs.Current?.State);
        Assert.Equal(1L, this.runs.Current?.CopiedPayloadCount);
    }

    /// <summary>An endpoint answering with more than the row records meets the ceiling, and what came back is the mismatch it is.</summary>
    [Fact]
    public async Task RunAsync_ObjectComesBackLongerThanTheRowRecords_ReadsNoFurtherThanTheCeiling()
    {
        // Arrange
        this.ArrangeRunningMove();
        this.content.Arrange(EmailContentKind.IncomingMessage, PayloadId(1), Payload(1, byteLength: 8));
        this.objects.CorruptedReadBack = Payload(1, byteLength: 4096);

        // Act
        var pass = await this.MoveOver().RunAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(8L, this.objects.ReadBackCeiling);
        Assert.Equal(1L, pass.FailedPayloadCount);
        Assert.Equal(StoredContentMoveFailure.ObjectMismatch, Assert.Single(this.telemetry.Failures));
        Assert.Empty(this.content.Repoints);
    }

    /// <summary>An endpoint that cannot be written to writes nothing, and the row it was about to hold stays in the database.</summary>
    [Fact]
    public async Task RunAsync_EndpointCannotAnswerTheWrite_LeavesTheRowInTheDatabase()
    {
        // Arrange
        this.ArrangeRunningMove();
        this.ArrangePayload(EmailContentKind.IncomingMessage, 1);
        this.objects.IsUnavailable = true;

        // Act
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            this.MoveOver().RunAsync(TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(0, this.objects.PlacementCount);
        Assert.Empty(this.content.Repoints);
        Assert.Equal(0L, this.runs.Current?.CopiedPayloadCount);
    }

    /// <summary>An endpoint that goes away between the write and the read-back leaves the row exactly where it was.</summary>
    [Fact]
    public async Task RunAsync_EndpointCannotAnswerTheReadBack_LeavesTheRowInTheDatabase()
    {
        // Arrange
        this.ArrangeRunningMove();
        this.ArrangePayload(EmailContentKind.IncomingMessage, 1);
        this.objects.WhenPlacing = _ => this.objects.IsUnavailable = true;

        // Act
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            this.MoveOver().RunAsync(TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(1, this.objects.PlacementCount);
        Assert.Empty(this.content.Repoints);
        Assert.Equal(0L, this.runs.Current?.CopiedPayloadCount);
    }

    /// <summary>A pause reaches the pass that is running, which ends after the payload in flight rather than at its own ceilings.</summary>
    [Fact]
    public async Task RunAsync_PausedMidPass_EndsAfterThePayloadInFlightRatherThanAtTheCeiling()
    {
        // Arrange
        this.ArrangeRunningMove();
        this.ArrangePayloads(EmailContentKind.IncomingMessage, count: 5);
        this.runs.ArrangeDecisionOnRead(2, run => run with { State = StoredContentMoveState.Paused });

        // Act
        var pass = await this.MoveOver(payloadsPerPass: 20).RunAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1L, pass.CopiedPayloadCount);
        Assert.True(pass.PayloadsRemain);
        Assert.Equal(1, this.objects.PlacementCount);
        Assert.Equal(PayloadId(1), this.runs.Current?.ResumeAfter);
    }

    /// <summary>A shutdown that interrupts one payload keeps everything the pass had already carried.</summary>
    [Fact]
    public async Task RunAsync_CancelledWhileCarryingAPayload_StillRecordsWhatItAlreadyMoved()
    {
        // Arrange
        this.ArrangeRunningMove();
        this.ArrangePayloads(EmailContentKind.IncomingMessage, count: 3);

        using CancellationTokenSource shutdown = new();
        this.objects.WhenPlacing = placement =>
        {
            if (placement == 2)
            {
                shutdown.Cancel();
            }
        };

        // Act
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            this.MoveOver().RunAsync(shutdown.Token));

        // Assert
        Assert.Equal(1L, this.runs.Current?.CopiedPayloadCount);
        Assert.Equal(PayloadId(1), this.runs.Current?.ResumeAfter);
        Assert.Single(this.content.Repoints);
    }

    /// <summary>A move that ended under the pass is left exactly as it ended rather than reopened by the pass's counts.</summary>
    [Fact]
    public async Task RunAsync_MoveCompletedWhileThePassRan_RecordsNothingOverIt()
    {
        // Arrange
        this.ArrangeRunningMove();
        this.ArrangePayload(EmailContentKind.IncomingMessage, 1);
        this.runs.ArrangeDecisionOnRead(2, run => run with
        {
            State = StoredContentMoveState.Completed,
            EndedAt = Moment,
        });

        // Act
        await this.MoveOver().RunAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StoredContentMoveState.Completed, this.runs.Current?.State);
        Assert.Equal(0L, this.runs.Current?.CopiedPayloadCount);
        Assert.Empty(this.runs.Saves);
    }

    /// <summary>Nothing but this deployment's own process may walk its mail, whatever grant a caller carries.</summary>
    [Fact]
    public async Task RunAsync_ReachedByACallerRatherThanTheProcess_IsRefused()
    {
        // Arrange
        this.ArrangeRunningMove();
        this.ArrangePayload(EmailContentKind.IncomingMessage, 1);

        var move = this.MoveOver(
            authorization: AccessAuthorizations.ForCallerGranted(
                MailFathomPermission.AdminOperate,
                MailFathomPermission.AdminRead));

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() =>
            move.RunAsync(TestContext.Current.CancellationToken));

        // Assert
        Assert.False(refusal.RequiredPermission.IsSpecified);
        Assert.Empty(this.content.Repoints);
    }

    /// <summary>Builds a payload identity whose order is the order the walk visits it in.</summary>
    private static Guid PayloadId(int position) => Guid.Parse($"00000000-0000-0000-0000-{position:D12}");

    private void ArrangeRunningMove(StoredContentMoveState state = StoredContentMoveState.Running) =>
        this.runs.Arrange(new StoredContentMoveRun
        {
            RequestedAt = Moment,
            State = state,
            Kind = EmailContentKind.IncomingMessage,
        });

    private void ArrangePayload(EmailContentKind kind, int position) =>
        this.content.Arrange(kind, PayloadId(position), [(byte)position, 2, 3, 4]);

    private void ArrangePayloads(EmailContentKind kind, int count, int byteLength = 4)
    {
        foreach (var position in Enumerable.Range(1, count))
        {
            this.content.Arrange(kind, PayloadId(position), Payload(position, byteLength));
        }
    }

    /// <summary>Builds a payload of a stated size whose bytes differ per position, so no two objects are the same message.</summary>
    private static byte[] Payload(int position, int byteLength)
    {
        var payload = new byte[byteLength];
        payload[0] = (byte)position;

        return payload;
    }

    private StoredContentMove MoveOver(
        int payloadsPerPass = 20,
        long maxBytesPerPass = 64L * 1024 * 1024,
        long memoryBudgetBytes = 64L * 1024 * 1024,
        AccessAuthorization? authorization = null)
    {
        var sessionFactory = Substitute.For<IPersistenceSessionFactory>();
        sessionFactory.BeginSessionAsync(Arg.Any<CancellationToken>()).Returns(_ => new CommittingSession());

        FakeTimeProvider timeProvider = new(Moment);

        return new StoredContentMove(
            this.runs,
            this.content,
            this.objects,
            new RawMimeMemoryBudget(memoryBudgetBytes),
            new OptimisticConcurrencyRetryPolicy(
                sessionFactory,
                new PersistenceConcurrencyOptions { MaximumCommitAttempts = 2 },
                timeProvider),
            this.telemetry,
            new StoredContentMoveOptions
            {
                PayloadsPerPass = payloadsPerPass,
                MaxBytesPerPass = maxBytesPerPass,
            },
            timeProvider,
            authorization ?? AccessAuthorizations.ForPrincipal(AuthorizedPrincipal.Process));
    }

    private sealed class CommittingSession : IPersistenceSession
    {
        public Task<PersistenceCommitResult> CommitAsync(CancellationToken cancellationToken) =>
            Task.FromResult(PersistenceCommitResult.Committed);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
