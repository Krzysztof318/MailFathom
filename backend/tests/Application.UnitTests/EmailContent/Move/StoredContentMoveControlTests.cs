// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

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

/// <summary>Covers the three decisions an operator takes about the move, and the grants they take them under.</summary>
public sealed class StoredContentMoveControlTests
{
    private static readonly DateTimeOffset Moment = new(2026, 8, 24, 12, 0, 0, TimeSpan.Zero);

    private readonly InMemoryStoredContentMoveRunStore runs = new();

    /// <summary>A deployment that has never moved its content gets a move that starts at the first payload kind.</summary>
    [Fact]
    public async Task StartAsync_NoMoveYet_RecordsOneRunningFromTheBeginning()
    {
        // Arrange
        var control = this.ControlOver();

        // Act
        var run = await control.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StoredContentMoveState.Running, run.State);
        Assert.Equal(EmailContentKind.IncomingMessage, run.Kind);
        Assert.Null(run.ResumeAfter);
        Assert.Equal(Moment, run.RequestedAt);
    }

    /// <summary>Asking twice is asking once, so a second request answers with the move already under way.</summary>
    [Fact]
    public async Task StartAsync_MoveAlreadyRunning_AnswersWithItAndRecordsNothing()
    {
        // Arrange
        var began = Moment.AddHours(-1);
        this.Arrange(began, StoredContentMoveState.Running, copied: 12);

        // Act
        var run = await this.ControlOver().StartAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(began, run.RequestedAt);
        Assert.Equal(12L, run.CopiedPayloadCount);
        Assert.Empty(this.runs.Saves);
    }

    /// <summary>A paused move is never started over, because that would discard the position its operator stopped at.</summary>
    [Fact]
    public async Task StartAsync_MovePaused_LeavesItPausedRatherThanWalkingItAgain()
    {
        // Arrange
        var began = Moment.AddHours(-1);
        this.Arrange(began, StoredContentMoveState.Paused, copied: 12);

        // Act
        var run = await this.ControlOver().StartAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StoredContentMoveState.Paused, run.State);
        Assert.Equal(began, run.RequestedAt);
        Assert.Empty(this.runs.Saves);
    }

    /// <summary>A finished move is what a further one is asked for past, which is how refused payloads are reached again.</summary>
    [Fact]
    public async Task StartAsync_LastMoveFinished_RecordsAFreshOne()
    {
        // Arrange
        this.Arrange(Moment.AddDays(-1), StoredContentMoveState.Completed, copied: 12, endedAt: Moment.AddDays(-1));

        // Act
        var run = await this.ControlOver().StartAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StoredContentMoveState.Running, run.State);
        Assert.Equal(Moment, run.RequestedAt);
        Assert.Equal(0L, run.CopiedPayloadCount);
        Assert.Null(run.EndedAt);
    }

    /// <summary>A deployment with no object backend is refused rather than given a move that would carry nothing.</summary>
    [Fact]
    public async Task StartAsync_NoObjectBackendConfigured_IsRefused()
    {
        // Arrange
        var control = this.ControlOver(withObjectBackend: false);

        // Act
        var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            control.StartAsync(TestContext.Current.CancellationToken));

        // Assert
        Assert.False(control.IsAvailable);
        Assert.Contains("ContentStorage:ObjectStorage", refusal.Message, StringComparison.Ordinal);
        Assert.Empty(this.runs.Saves);
    }

    /// <summary>Rewriting where a deployment holds its mail is work it performs on request, and asks for that grant.</summary>
    [Fact]
    public async Task StartAsync_WithoutTheOperateGrant_IsRefused()
    {
        // Arrange
        var control = this.ControlOver(
            authorization: AccessAuthorizations.ForCallerGranted(MailFathomPermission.AdminRead));

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() =>
            control.StartAsync(TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomPermission.AdminOperate, refusal.RequiredPermission);
        Assert.Empty(this.runs.Saves);
    }

    /// <summary>Pausing stops the move where it is and keeps everything it has carried.</summary>
    [Fact]
    public async Task PauseAsync_MoveRunning_StopsItWhereItIs()
    {
        // Arrange
        this.Arrange(Moment, StoredContentMoveState.Running, copied: 12);

        // Act
        var run = await this.ControlOver().PauseAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StoredContentMoveState.Paused, run?.State);
        Assert.Equal(12L, run?.CopiedPayloadCount);
    }

    /// <summary>A deployment nobody asked for a move answers with nothing rather than recording one to stop.</summary>
    [Fact]
    public async Task PauseAsync_NoMoveYet_AnswersWithNothing()
    {
        // Act
        var run = await this.ControlOver().PauseAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(run);
        Assert.Empty(this.runs.Saves);
    }

    /// <summary>A finished move has nothing to stop, and saying so is the answer rather than a refusal.</summary>
    [Fact]
    public async Task PauseAsync_MoveFinished_LeavesItAsItEnded()
    {
        // Arrange
        this.Arrange(Moment.AddDays(-1), StoredContentMoveState.Completed, copied: 12, endedAt: Moment);

        // Act
        var run = await this.ControlOver().PauseAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StoredContentMoveState.Completed, run?.State);
        Assert.Empty(this.runs.Saves);
    }

    /// <summary>Resuming sets a stopped move going again from where it stopped rather than from the beginning.</summary>
    [Fact]
    public async Task ResumeAsync_MovePaused_SetsItGoingFromWhereItStopped()
    {
        // Arrange
        var position = Guid.Parse("00000000-0000-0000-0000-000000000007");
        this.runs.Arrange(new StoredContentMoveRun
        {
            RequestedAt = Moment,
            State = StoredContentMoveState.Paused,
            Kind = EmailContentKind.OutgoingMessage,
            ResumeAfter = position,
            CopiedPayloadCount = 12,
        });

        // Act
        var run = await this.ControlOver().ResumeAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StoredContentMoveState.Running, run?.State);
        Assert.Equal(EmailContentKind.OutgoingMessage, run?.Kind);
        Assert.Equal(position, run?.ResumeAfter);
    }

    /// <summary>A finished move is not resumed: what reaches what it left behind is a further move.</summary>
    [Fact]
    public async Task ResumeAsync_MoveFinished_LeavesItFinished()
    {
        // Arrange
        this.Arrange(Moment.AddDays(-1), StoredContentMoveState.Completed, copied: 12, endedAt: Moment);

        // Act
        var run = await this.ControlOver().ResumeAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StoredContentMoveState.Completed, run?.State);
        Assert.Empty(this.runs.Saves);
    }

    /// <summary>A deployment whose endpoint was taken away is refused rather than set going against nothing.</summary>
    [Fact]
    public async Task ResumeAsync_NoObjectBackendConfigured_IsRefused()
    {
        // Arrange
        this.Arrange(Moment, StoredContentMoveState.Paused, copied: 12);

        // Act
        var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            this.ControlOver(withObjectBackend: false).ResumeAsync(TestContext.Current.CancellationToken));

        // Assert
        Assert.Contains("ContentStorage:ObjectStorage", refusal.Message, StringComparison.Ordinal);
        Assert.Empty(this.runs.Saves);
    }

    private void Arrange(
        DateTimeOffset requestedAt,
        StoredContentMoveState state,
        long copied,
        DateTimeOffset? endedAt = null) =>
        this.runs.Arrange(new StoredContentMoveRun
        {
            RequestedAt = requestedAt,
            State = state,
            Kind = EmailContentKind.IncomingMessage,
            CopiedPayloadCount = copied,
            EndedAt = endedAt,
        });

    private StoredContentMoveControl ControlOver(
        bool withObjectBackend = true,
        AccessAuthorization? authorization = null)
    {
        var sessionFactory = Substitute.For<IPersistenceSessionFactory>();
        sessionFactory.BeginSessionAsync(Arg.Any<CancellationToken>()).Returns(_ => new CommittingSession());

        FakeTimeProvider timeProvider = new(Moment);

        return new StoredContentMoveControl(
            this.runs,
            new OptimisticConcurrencyRetryPolicy(
                sessionFactory,
                new PersistenceConcurrencyOptions { MaximumCommitAttempts = 2 },
                timeProvider),
            timeProvider,
            authorization ?? AccessAuthorizations.ForCallerGranted(MailFathomPermission.AdminOperate),
            withObjectBackend ? new InMemoryEmailContentObjectBackend() : null);
    }

    private sealed class CommittingSession : IPersistenceSession
    {
        public Task<PersistenceCommitResult> CommitAsync(CancellationToken cancellationToken) =>
            Task.FromResult(PersistenceCommitResult.Committed);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
