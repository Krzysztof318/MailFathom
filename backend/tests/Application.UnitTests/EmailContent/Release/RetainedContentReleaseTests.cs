// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.EmailContent.Release;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Access;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace MailFathom.Application.UnitTests.EmailContent.Release;

/// <summary>Covers the one irreversible step of the move: freeing the copies the database kept beside verified objects.</summary>
/// <remarks>
/// What is asserted here is when the release refuses, what one bounded request spends, and what it publishes. Which rows
/// a batch selects is the store's own question and is proven against a real database, so the store is a fake here and
/// what the tests read out of it is the cutoff and the bound each kind was asked under.
/// </remarks>
public sealed class RetainedContentReleaseTests
{
    private static readonly DateTimeOffset Moment = new(2026, 8, 25, 12, 0, 0, TimeSpan.Zero);

    private readonly InMemoryRetainedContentReleaseStore retained = new();
    private readonly InMemoryStoredContentMoveStore content = new();
    private readonly RecordingRetainedContentReleaseTelemetry telemetry = new();

    /// <summary>A deployment the move has not finished carrying keeps every copy, because the move is what the copies protect.</summary>
    [Fact]
    public async Task ReleaseAsync_ContentStillAwaitingTheMove_FreesNothingAndNamesTheBacklog()
    {
        // Arrange
        this.Retain(EmailContentKind.IncomingMessage, 1, byteLength: 900);
        this.content.Arrange(EmailContentKind.IncomingMessage, PayloadId(2), rawMime: [1, 2, 3]);

        // Act
        var result = await this.Release().ReleaseAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.WasRefused);
        Assert.Equal(1L, result.AwaitingMove.PayloadCount);
        Assert.Equal(0L, result.Released.PayloadCount);
        Assert.Equal(1L, result.Retained.PayloadCount);
        Assert.Empty(this.retained.Batches);
        Assert.Empty(this.telemetry.Releases);
    }

    /// <summary>One request spends its bound across the payload kinds and answers with what is left for the next one.</summary>
    [Fact]
    public async Task ReleaseAsync_EverythingCarried_SpendsOneBoundAcrossThePayloadKinds()
    {
        // Arrange
        this.Retain(EmailContentKind.IncomingMessage, 1, byteLength: 700);
        this.Retain(EmailContentKind.IncomingMessage, 2, byteLength: 300);
        this.Retain(EmailContentKind.OutgoingMessage, 3, byteLength: 500);
        this.Retain(EmailContentKind.MailDraft, 4, byteLength: 100);

        // Act
        var result = await this.Release(new RetainedContentReleaseOptions { PayloadsPerBatch = 3 })
            .ReleaseAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result.WasRefused);
        Assert.Equal(3L, result.Released.PayloadCount);
        Assert.Equal(1500L, result.Released.ByteCount);
        Assert.True(result.PayloadsRemain);
        Assert.Equal(1L, result.Retained.PayloadCount);
        Assert.Equal(
            [
                (EmailContentKind.IncomingMessage, 3),
                (EmailContentKind.OutgoingMessage, 1),
            ],
            this.retained.Batches.Select(batch => (batch.Kind, batch.BatchSize)));
    }

    /// <summary>The safety interval is a floor beneath the operator's own decision, so a copy inside it is left alone.</summary>
    [Fact]
    public async Task ReleaseAsync_CopyVerifiedInsideTheSafetyInterval_LeavesItForALaterRequest()
    {
        // Arrange
        this.Retain(EmailContentKind.IncomingMessage, 1, byteLength: 400, verifiedAt: Moment.AddHours(-2));
        this.Retain(EmailContentKind.IncomingMessage, 2, byteLength: 600, verifiedAt: Moment.AddMinutes(-30));

        // Act
        var result = await this.Release(new RetainedContentReleaseOptions { SafetyInterval = TimeSpan.FromHours(1) })
            .ReleaseAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1L, result.Released.PayloadCount);
        Assert.Equal(400L, result.Released.ByteCount);
        Assert.Equal(1L, result.Retained.PayloadCount);
        Assert.All(this.retained.Batches, batch => Assert.Equal(Moment.AddHours(-1), batch.VerifiedOnOrBefore));
    }

    /// <summary>A request that freed nothing publishes nothing, so the counter reads as copies freed rather than as requests made.</summary>
    [Fact]
    public async Task ReleaseAsync_NothingRetained_PublishesNoRelease()
    {
        // Act
        var result = await this.Release().ReleaseAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0L, result.Released.PayloadCount);
        Assert.False(result.PayloadsRemain);
        Assert.Empty(this.telemetry.Releases);
    }

    /// <summary>What was freed is published as both figures, because the volume is the whole point of the operation.</summary>
    [Fact]
    public async Task ReleaseAsync_CopiesFreed_PublishesTheCountAndTheBytes()
    {
        // Arrange
        this.Retain(EmailContentKind.IncomingMessage, 1, byteLength: 700);
        this.Retain(EmailContentKind.RecurringSendDraft, 2, byteLength: 300);

        // Act
        await this.Release().ReleaseAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([(1L, 700L), (1L, 300L)], this.telemetry.Releases);
    }

    /// <summary>A copy freed is gone whether or not the request finished, so what the earlier kinds freed is published anyway.</summary>
    [Fact]
    public async Task ReleaseAsync_CancelledAtALaterPayloadKind_PublishesWhatWasAlreadyFreed()
    {
        // Arrange
        this.Retain(EmailContentKind.IncomingMessage, 1, byteLength: 700);
        this.Retain(EmailContentKind.OutgoingMessage, 2, byteLength: 300);
        this.retained.CancelOnReaching(EmailContentKind.OutgoingMessage);

        // Act
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            this.Release().ReleaseAsync(TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal([(1L, 700L)], this.telemetry.Releases);
    }

    /// <summary>Disposing of the last copy of a message is the erasing grant's, and the grant that asked for the move will not do.</summary>
    [Fact]
    public async Task ReleaseAsync_WithoutTheEraseGrant_IsRefused()
    {
        // Arrange
        this.Retain(EmailContentKind.IncomingMessage, 1, byteLength: 700);

        var release = this.Release(
            authorization: AccessAuthorizations.ForCallerGranted(
                MailFathomPermission.AdminRead,
                MailFathomPermission.AdminOperate));

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() =>
            release.ReleaseAsync(TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomPermission.AdminErase, refusal.RequiredPermission);
        Assert.Empty(this.retained.Batches);
    }

    /// <summary>How much of a database is duplication is a question about what a deployment holds, and reading it frees nothing.</summary>
    [Fact]
    public async Task ReadAsync_CopiesRetained_ReportsThemWithoutFreeingAnything()
    {
        // Arrange
        this.Retain(EmailContentKind.IncomingMessage, 1, byteLength: 700);
        this.content.Arrange(EmailContentKind.OutgoingMessage, PayloadId(2), rawMime: [1, 2, 3, 4]);

        var release = this.Release(authorization: AccessAuthorizations.ForCallerGranted(MailFathomPermission.AdminRead));

        // Act
        var result = await release.ReadAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0L, result.Released.PayloadCount);
        Assert.Equal(1L, result.Retained.PayloadCount);
        Assert.Equal(700L, result.Retained.ByteCount);
        Assert.Equal(1L, result.AwaitingMove.PayloadCount);
        Assert.Empty(this.retained.Batches);
    }

    /// <summary>Reading what a deployment holds is still administration, and an ungranted caller is refused it.</summary>
    [Fact]
    public async Task ReadAsync_WithoutTheReadGrant_IsRefused()
    {
        // Arrange
        var release = this.Release(authorization: AccessAuthorizations.ForCallerGranted());

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() =>
            release.ReadAsync(TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomPermission.AdminRead, refusal.RequiredPermission);
    }

    private static Guid PayloadId(int number) => new($"00000000-0000-0000-0000-{number:D12}");

    private void Retain(EmailContentKind kind, int number, long byteLength, DateTimeOffset? verifiedAt = null) =>
        this.retained.Arrange(kind, PayloadId(number), byteLength, verifiedAt ?? Moment.AddDays(-1));

    private RetainedContentRelease Release(
        RetainedContentReleaseOptions? options = null,
        AccessAuthorization? authorization = null) =>
        new(
            this.retained,
            this.content,
            this.telemetry,
            options ?? new RetainedContentReleaseOptions(),
            new FakeTimeProvider(Moment),
            authorization ?? AccessAuthorizations.ForCallerGranted(MailFathomPermission.AdminErase));
}
