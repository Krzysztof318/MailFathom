// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text;
using MailFathom.Infrastructure.Secrets.Resolution;
using MailFathom.Infrastructure.Secrets.Sources;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Secrets.Sources;

/// <summary>Covers the read that turns an opened target into material, including the targets that are not files.</summary>
/// <remarks>
/// Every case here is a stream, which is what makes the file reader's whole contract reachable without a file system:
/// a regular file, a device, and a pipe differ from one another only in what an opened handle reports and yields.
/// </remarks>
public sealed class BoundedSecretMaterialReaderTests
{
    [Fact]
    public async Task ReadAsync_ForAnOrdinaryFile_ReturnsWhatItHeld()
    {
        // Arrange
        var material = "provisioned-password"u8.ToArray();
        using var source = new MemoryStream(material, writable: false);

        // Act
        var result = await BoundedSecretMaterialReader.ReadAsync(
            source,
            SecretMaterialLimits.MaximumMaterialByteCount,
            TestContext.Current.CancellationToken);

        // Assert
        using var secret = result.Secret;
        Assert.Equal(material, secret!.RevealBytes().ToArray());
    }

    /// <summary>A FIFO, a socket, and a terminal all present as a target no seek reaches, and none of them holds a credential.</summary>
    [Fact]
    public async Task ReadAsync_ForATargetThatCannotSeek_ReportsTargetNotRegularFile()
    {
        // Arrange
        await using var source = new PipeLikeStream();

        // Act
        var result = await BoundedSecretMaterialReader.ReadAsync(
            source,
            SecretMaterialLimits.MaximumMaterialByteCount,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SecretResolutionFailure.TargetNotRegularFile, result.Failure);
        Assert.Null(result.Secret);
    }

    /// <summary>A character device such as <c>/dev/zero</c> is seekable and reports no length, yet yields bytes without end.</summary>
    /// <remarks>
    /// The identity matters as much as the refusal. Judged by size alone the same target reports oversized material,
    /// which sends an operator looking for a secret too large to use rather than for a path that names a device.
    /// </remarks>
    [Fact]
    public async Task ReadAsync_ForATargetYieldingMoreThanItReports_ReportsTargetNotRegularFileRatherThanOversizedMaterial()
    {
        // Arrange
        await using var source = new DeviceLikeStream();

        // Act
        var result = await BoundedSecretMaterialReader.ReadAsync(
            source,
            SecretMaterialLimits.MaximumMaterialByteCount,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SecretResolutionFailure.TargetNotRegularFile, result.Failure);
        Assert.Null(result.Secret);
    }

    /// <summary>A credential source that disconnects after the target opened must not abort the aggregated startup report.</summary>
    [Fact]
    public async Task ReadAsync_SourceFailsAfterOpening_FailsWithProviderUnavailableRatherThanThrowing()
    {
        // Arrange
        await using var source = new FailingAfterOpenStream();

        // Act
        var result = await BoundedSecretMaterialReader.ReadAsync(
            source,
            SecretMaterialLimits.MaximumMaterialByteCount,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SecretResolutionFailure.ProviderUnavailable, result.Failure);
        Assert.Null(result.Secret);
    }

    [Fact]
    public async Task ReadAsync_CancelledWhileReading_PropagatesCancellationInsteadOfReportingAProviderFailure()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        await using var source = new FailingAfterOpenStream();
        await cancellation.CancelAsync();

        // Act, Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => BoundedSecretMaterialReader.ReadAsync(
            source,
            SecretMaterialLimits.MaximumMaterialByteCount,
            cancellation.Token));
    }

    /// <summary>A readable stream shaped like a regular file, so what a test varies is the behaviour rather than the type.</summary>
    private abstract class SecretTargetStream : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => true;

        public override bool CanWrite => false;

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override void Flush()
        {
        }
    }

    /// <summary>A target the platform will not seek, which is how a FIFO, a socket, and a terminal all present.</summary>
    private sealed class PipeLikeStream : SecretTargetStream
    {
        public override bool CanSeek => false;

        public override long Length => throw new NotSupportedException();

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("A target that cannot seek must be refused before it is read.");
    }

    /// <summary>A target reporting no length while yielding bytes without end, which is how a character device presents.</summary>
    private sealed class DeviceLikeStream : SecretTargetStream
    {
        public override long Length => 0;

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            buffer.Span.Clear();

            return ValueTask.FromResult(buffer.Length);
        }
    }

    /// <summary>A readable stream that fails the way a disconnected network mount does, after opening cleanly.</summary>
    private sealed class FailingAfterOpenStream : SecretTargetStream
    {
        public override long Length => Encoding.UTF8.GetByteCount("the material this mount was holding");

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            throw new IOException("The credential source became unreachable.");
        }
    }
}
