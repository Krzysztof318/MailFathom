// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Secrets.Resolution;
using MailFathom.Infrastructure.Secrets.Sources;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Secrets.Sources;

/// <summary>Covers the deadline around an open the platform will not return from, and the bound on how many may stall.</summary>
/// <remarks>
/// The open is a delegate here, which is the whole reason these are unit tests: a FIFO with no writer and a stalled
/// network mount are both, from this side, a call that has not come back yet, and a delegate reproduces that exactly
/// while a real one would need a file system and a real five seconds.
/// </remarks>
public sealed class BoundedSecretFileRetrievalTests
{
    /// <summary>How long an assertion waits for work on another thread before calling the test hung rather than slow.</summary>
    private static readonly TimeSpan WaitLimit = TimeSpan.FromMinutes(1);

    [Fact]
    public async Task ReadAsync_ForAnOpenedTarget_ReturnsTheMaterialItHeld()
    {
        // Arrange
        var material = "provisioned-password"u8.ToArray();
        using var retrieval = new BoundedSecretFileRetrieval(new FakeTimeProvider());

        // Act
        var result = await retrieval.ReadAsync(
            () => new MemoryStream(material, writable: false),
            SecretMaterialLimits.MaximumMaterialByteCount,
            TestContext.Current.CancellationToken);

        // Assert
        using var secret = result.Secret;
        Assert.Equal(material, secret!.RevealBytes().ToArray());
    }

    [Fact]
    public async Task ReadAsync_ForATargetTheFileSystemRefused_ReportsMaterialNotFound()
    {
        // Arrange
        using var retrieval = new BoundedSecretFileRetrieval(new FakeTimeProvider());

        // Act
        var result = await retrieval.ReadAsync(
            () => null,
            SecretMaterialLimits.MaximumMaterialByteCount,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SecretResolutionFailure.MaterialNotFound, result.Failure);
        Assert.Null(result.Secret);
    }

    /// <summary>The defect this bound exists for: an open the kernel does not return from must fail rather than wait.</summary>
    [Fact]
    public async Task ReadAsync_WhenTheOpenDoesNotReturnBeforeTheDeadline_ReportsRetrievalTimedOut()
    {
        // Arrange
        var clock = new FakeTimeProvider();
        using var stalledOpen = new StalledOpen();
        using var retrieval = new BoundedSecretFileRetrieval(clock);

        var reading = retrieval.ReadAsync(
            stalledOpen.Open,
            SecretMaterialLimits.MaximumMaterialByteCount,
            TestContext.Current.CancellationToken);

        stalledOpen.WaitUntilEntered();

        // Act
        clock.Advance(SecretMaterialLimits.RetrievalDeadline);

        // Assert
        var result = await reading;

        Assert.Equal(SecretResolutionFailure.RetrievalTimedOut, result.Failure);
        Assert.Null(result.Secret);
    }

    /// <summary>What the deadline costs is a thread nobody can interrupt, so the number of them is what is bounded.</summary>
    /// <remarks>
    /// The assertion is that the retrieval past the bound never reaches the platform at all. That is the whole
    /// protection: a target the storage will never answer for stops costing a thread once enough of them are stuck.
    /// </remarks>
    [Fact]
    public async Task ReadAsync_WhenEveryPermitIsHeldByAStalledOpen_TimesOutWithoutEnteringThePlatform()
    {
        // Arrange
        var clock = new FakeTimeProvider();
        using var stalledOpens = new StalledOpen();
        using var retrieval = new BoundedSecretFileRetrieval(clock);

        var stalled = Enumerable
            .Range(0, SecretMaterialLimits.MaximumConcurrentRetrievalCount)
            .Select(_ => retrieval.ReadAsync(
                stalledOpens.Open,
                SecretMaterialLimits.MaximumMaterialByteCount,
                TestContext.Current.CancellationToken))
            .ToArray();

        stalledOpens.WaitUntilEntered(SecretMaterialLimits.MaximumConcurrentRetrievalCount);
        clock.Advance(SecretMaterialLimits.RetrievalDeadline);
        await Task.WhenAll(stalled);

        var refusedOpenCount = 0;

        // Act
        var reading = retrieval.ReadAsync(
            () =>
            {
                Interlocked.Increment(ref refusedOpenCount);

                return null;
            },
            SecretMaterialLimits.MaximumMaterialByteCount,
            TestContext.Current.CancellationToken);

        clock.Advance(SecretMaterialLimits.RetrievalDeadline);

        // Assert
        var result = await reading;

        Assert.Equal(SecretResolutionFailure.RetrievalTimedOut, result.Failure);
        Assert.Equal(0, refusedOpenCount);
    }

    /// <summary>A stream that arrives after nobody is waiting for it is still a handle, and it is closed rather than leaked.</summary>
    [Fact]
    public async Task ReadAsync_WhenAnAbandonedOpenReturnsAfterTheDeadline_DisposesTheStreamNobodyIsWaitingFor()
    {
        // Arrange
        var clock = new FakeTimeProvider();
        using var stalledOpen = new StalledOpen();
        using var abandonedTarget = new DisposalAnnouncingStream();
        using var retrieval = new BoundedSecretFileRetrieval(clock);

        var reading = retrieval.ReadAsync(
            () =>
            {
                stalledOpen.Open();

                return abandonedTarget;
            },
            SecretMaterialLimits.MaximumMaterialByteCount,
            TestContext.Current.CancellationToken);

        stalledOpen.WaitUntilEntered();
        clock.Advance(SecretMaterialLimits.RetrievalDeadline);

        var result = await reading;

        // Act
        stalledOpen.LetTheOpensReturn();

        // Assert
        Assert.Equal(SecretResolutionFailure.RetrievalTimedOut, result.Failure);
        await abandonedTarget.WaitUntilDisposedAsync().WaitAsync(WaitLimit, TestContext.Current.CancellationToken);
    }

    /// <summary>An open that finished, however badly, has not stalled, so the place it holds must come back with it.</summary>
    /// <remarks>
    /// Losing a permit to an exception rather than to a stall is the failure worth a test of its own: it looks like
    /// nothing at all until enough of them have happened, and then every secret reference reports a deadline that no
    /// storage caused.
    /// </remarks>
    [Fact(Timeout = 60_000)]
    public async Task ReadAsync_WhenTheOpenFailsInAWayTheAdapterDoesNotTranslate_GivesItsPermitBack()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        using var retrieval = new BoundedSecretFileRetrieval(new FakeTimeProvider());

        for (var failure = 0; failure <= SecretMaterialLimits.MaximumConcurrentRetrievalCount; failure++)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => retrieval.ReadAsync(
                () => throw new InvalidOperationException("An untranslated file-system failure."),
                SecretMaterialLimits.MaximumMaterialByteCount,
                cancellationToken));
        }

        // Act
        var result = await retrieval.ReadAsync(
            () => null,
            SecretMaterialLimits.MaximumMaterialByteCount,
            cancellationToken);

        // Assert
        Assert.Equal(SecretResolutionFailure.MaterialNotFound, result.Failure);
    }

    [Fact]
    public async Task ReadAsync_WhenTheCallerCancels_PropagatesCancellationRatherThanReportingADeadline()
    {
        // Arrange
        var clock = new FakeTimeProvider();
        using var cancellation = new CancellationTokenSource();
        using var stalledOpen = new StalledOpen();
        using var retrieval = new BoundedSecretFileRetrieval(clock);

        var reading = retrieval.ReadAsync(
            stalledOpen.Open,
            SecretMaterialLimits.MaximumMaterialByteCount,
            cancellation.Token);

        stalledOpen.WaitUntilEntered();

        // Act
        await cancellation.CancelAsync();

        // Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reading);
    }

    /// <summary>An open that has entered the platform and not come back, which is what a FIFO and a dead mount both are.</summary>
    private sealed class StalledOpen : IDisposable
    {
        private readonly SemaphoreSlim entered = new(0);
        private readonly ManualResetEventSlim returns = new();

        public Stream? Open()
        {
            this.entered.Release();
            this.returns.Wait();

            return null;
        }

        public void WaitUntilEntered(int openCount = 1)
        {
            for (var pending = 0; pending < openCount; pending++)
            {
                Assert.True(this.entered.Wait(WaitLimit));
            }
        }

        public void LetTheOpensReturn() => this.returns.Set();

        public void Dispose()
        {
            this.returns.Set();
            this.returns.Dispose();
            this.entered.Dispose();
        }
    }

    /// <summary>A stream that says when it was closed, so an assertion waits for the disposal rather than for a duration.</summary>
    private sealed class DisposalAnnouncingStream : MemoryStream
    {
        private readonly TaskCompletionSource disposed = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task WaitUntilDisposedAsync() => this.disposed.Task;

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            this.disposed.TrySetResult();
        }
    }
}
