// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net;
using System.Text;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using MailFathom.Domain.Exports;
using MailFathom.Infrastructure.ObjectStorage;
using MailFathom.Infrastructure.Observability;
using MailFathom.Infrastructure.UnitTests.TestDoubles;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.ObjectStorage;

/// <summary>Covers the one streamed write in this system: what the archive is named, when a part leaves, and what an abandoned write leaves behind.</summary>
/// <remarks>
/// The claims worth proving are the ones a partial archive would break. Nothing is readable under the key until the
/// upload is completed, a write that ended without completing aborts rather than leaving parts the endpoint charges
/// for, and every part but the last is the whole part size — an endpoint refuses a short middle part, which would turn
/// a finished export into a failure at the very end of a mailbox.
/// </remarks>
public sealed class S3MailboxExportArchiveStoreTests
{
    /// <summary>The part size the write gathers to, which is what decides when a part leaves this process.</summary>
    private const int PartByteLength = 8 * 1024 * 1024;

    private static readonly ObjectStorageEndpoint Endpoint = ObjectStorageEndpoint.Create(
        new Uri("https://objects.example.test:9000/"),
        "payloads",
        "mailfathom",
        "eu-central-1",
        usePathStyleAddressing: true,
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(100));

    private static readonly MailboxExportId Export =
        MailboxExportId.Create(new Guid("0199a0c0-0000-7000-8000-000000000005"));

    /// <summary>
    /// The store is registered whatever the deployment keeps content in, so a deployment on the database backend reads
    /// availability rather than meeting a failure — which is what lets the use case refuse the export naming a setting.
    /// </summary>
    [Fact]
    public void IsAvailable_ADeploymentKeepingContentInItsDatabase_ReportsThatThereIsNowhereToKeepAnArchive()
    {
        // Act
        var store = new S3MailboxExportArchiveStore();

        // Assert
        Assert.False(store.IsAvailable);
    }

    /// <summary>Reaching the endpoint without one is a defect in a caller rather than a deployment's configuration, so it raises.</summary>
    [Fact]
    public async Task BeginWriteAsync_ADeploymentKeepingContentInItsDatabase_IsRefusedRatherThanReachingAnEndpoint()
    {
        // Arrange
        var store = new S3MailboxExportArchiveStore();

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.BeginWriteAsync(Export, TestContext.Current.CancellationToken));
    }

    /// <summary>The key is the export's own identity under this deployment's prefix, and the object is a zip to whoever fetches it.</summary>
    [Fact]
    public async Task BeginWriteAsync_AnExport_InitiatesAnUploadUnderTheKeyItAnswersAndDeclaresTheArchiveAZip()
    {
        // Arrange
        var bucket = BucketAnswering();
        using var host = OutboundResilienceTestHost.WithConfiguredSettings();
        var store = StoreOver(bucket, host);

        // Act
        await using var write = await store.BeginWriteAsync(Export, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal($"mailfathom/mailbox-exports/{Export.Value}", write.ObjectLocator);
        await bucket.Received(1).InitiateMultipartUploadAsync(
            Arg.Is<InitiateMultipartUploadRequest>(request =>
                request != null
                && request.BucketName == "payloads"
                && request.Key == $"mailfathom/mailbox-exports/{Export.Value}"
                && request.ContentType == "application/zip"),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// An upload completed with no part is not an object, so an archive small enough never to have filled one still
    /// sends the last part. Completing answers what the archive holds, which is what the export's record carries.
    /// </summary>
    [Fact]
    public async Task CompleteAsync_AnArchiveSmallerThanOnePart_SendsItAsTheLastPartAndAnswersItsLength()
    {
        // Arrange
        var bucket = BucketAnswering();
        using var host = OutboundResilienceTestHost.WithConfiguredSettings();
        var store = StoreOver(bucket, host);
        var archive = Encoding.ASCII.GetBytes("PK\u0003\u0004 a whole small archive");

        // Act
        await using var write = await store.BeginWriteAsync(Export, TestContext.Current.CancellationToken);
        await write.Content.WriteAsync(archive, TestContext.Current.CancellationToken);
        var byteLength = await write.CompleteAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(archive.Length, byteLength);
        await bucket.Received(1).UploadPartAsync(
            Arg.Is<UploadPartRequest>(request =>
                request != null && request.PartNumber == 1 && request.PartSize == archive.Length),
            Arg.Any<CancellationToken>());
        await bucket.Received(1).CompleteMultipartUploadAsync(
            Arg.Is<CompleteMultipartUploadRequest>(request => request != null && request.PartETags.Count == 1),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Every part but the last is the whole part size, because an endpoint refuses a short middle part — and it refuses
    /// it at completion, which is the very end of a mailbox's worth of reading.
    /// </summary>
    [Fact]
    public async Task CompleteAsync_AnArchivePastOnePart_SendsWholePartsAsItFillsAndTheRemainderLast()
    {
        // Arrange
        var bucket = BucketAnswering();
        using var host = OutboundResilienceTestHost.WithConfiguredSettings();
        var store = StoreOver(bucket, host);
        const int remainder = 1024;
        var archive = new byte[PartByteLength + remainder];

        // Act
        await using var write = await store.BeginWriteAsync(Export, TestContext.Current.CancellationToken);
        await write.Content.WriteAsync(archive, TestContext.Current.CancellationToken);
        var byteLength = await write.CompleteAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(archive.Length, byteLength);
        await bucket.Received(1).UploadPartAsync(
            Arg.Is<UploadPartRequest>(request =>
                request != null && request.PartNumber == 1 && request.PartSize == PartByteLength),
            Arg.Any<CancellationToken>());
        await bucket.Received(1).UploadPartAsync(
            Arg.Is<UploadPartRequest>(request =>
                request != null && request.PartNumber == 2 && request.PartSize == remainder),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The zip writer emits every local header and the whole central directory synchronously, and no write here may
    /// block a thread on the network — so a synchronous write buffers and the next asynchronous one carries it.
    /// </summary>
    [Fact]
    public async Task Write_TheSynchronousWriteTheZipWriterUses_SendsNoPartUntilTheArchiveIsCompleted()
    {
        // Arrange
        var bucket = BucketAnswering();
        using var host = OutboundResilienceTestHost.WithConfiguredSettings();
        var store = StoreOver(bucket, host);

        // Act
        await using var write = await store.BeginWriteAsync(Export, TestContext.Current.CancellationToken);
        write.Content.Write("PK\u0003\u0004"u8);

        // Assert
        await bucket.DidNotReceive().UploadPartAsync(Arg.Any<UploadPartRequest>(), Arg.Any<CancellationToken>());

        // Act
        var byteLength = await write.CompleteAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(4, byteLength);
    }

    /// <summary>
    /// A job that stopped — a shutdown, a lost lease, a refusal past the size limit — abandons the upload, so the
    /// endpoint holds neither an object anybody could download nor the parts it would go on charging for.
    /// </summary>
    [Fact]
    public async Task DisposeAsync_AWriteNobodyCompleted_AbandonsTheUploadRatherThanLeavingItsParts()
    {
        // Arrange
        var bucket = BucketAnswering();
        using var host = OutboundResilienceTestHost.WithConfiguredSettings();
        var store = StoreOver(bucket, host);

        // Act
        var write = await store.BeginWriteAsync(Export, TestContext.Current.CancellationToken);
        await write.Content.WriteAsync("PK\u0003\u0004 an archive nobody finished"u8.ToArray(), TestContext.Current.CancellationToken);
        await write.DisposeAsync();

        // Assert
        await bucket.Received(1).AbortMultipartUploadAsync(
            Arg.Is<AbortMultipartUploadRequest>(request =>
                request != null && request.Key == $"mailfathom/mailbox-exports/{Export.Value}"),
            Arg.Any<CancellationToken>());
        await bucket.DidNotReceive().CompleteMultipartUploadAsync(
            Arg.Any<CompleteMultipartUploadRequest>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>A completed archive is the object, so disposing the write that produced it must not take it away again.</summary>
    [Fact]
    public async Task DisposeAsync_AWriteThatCompleted_LeavesTheArchiveWhereItIs()
    {
        // Arrange
        var bucket = BucketAnswering();
        using var host = OutboundResilienceTestHost.WithConfiguredSettings();
        var store = StoreOver(bucket, host);

        // Act
        var write = await store.BeginWriteAsync(Export, TestContext.Current.CancellationToken);
        await write.Content.WriteAsync("PK\u0003\u0004 a finished archive"u8.ToArray(), TestContext.Current.CancellationToken);
        await write.CompleteAsync(TestContext.Current.CancellationToken);
        await write.DisposeAsync();

        // Assert
        await bucket.DidNotReceive().AbortMultipartUploadAsync(
            Arg.Any<AbortMultipartUploadRequest>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>The download is the store's own stream the transport copies, so an archive never passes through this process whole.</summary>
    [Fact]
    public async Task OpenReadAsync_AnArchiveTheEndpointHolds_AnswersTheBodyItServed()
    {
        // Arrange
        var bucket = BucketAnswering();
        using var host = OutboundResilienceTestHost.WithConfiguredSettings();
        var store = StoreOver(bucket, host);

        // Act
        await using var content = await store.OpenReadAsync(
            $"mailfathom/mailbox-exports/{Export.Value}",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(content);
        using var reader = new StreamReader(content, Encoding.ASCII);
        Assert.Equal("archive", await reader.ReadToEndAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// A record saying an archive exists while the endpoint holds nothing under its key is a finding the caller grades,
    /// not a transport failure — so it is answered rather than raised, and nothing is retried against an absence.
    /// </summary>
    [Fact]
    public async Task OpenReadAsync_AKeyTheEndpointHoldsNothingUnder_AnswersNothingRatherThanFailing()
    {
        // Arrange
        var bucket = BucketAnswering();
        bucket.GetObjectAsync(Arg.Any<GetObjectRequest>(), Arg.Any<CancellationToken>())
            .Returns<Task<GetObjectResponse>>(_ => throw Absent());
        using var host = OutboundResilienceTestHost.WithConfiguredSettings();
        var store = StoreOver(bucket, host);

        // Act
        var content = await store.OpenReadAsync(
            $"mailfathom/mailbox-exports/{Export.Value}",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(content);
        await bucket.Received(1).GetObjectAsync(Arg.Any<GetObjectRequest>(), Arg.Any<CancellationToken>());
    }

    /// <summary>The key is the record's own, never recomposed, because a deletion under a recomposed key would remove somebody else's archive or nothing at all.</summary>
    [Fact]
    public async Task DeleteAsync_AnArchive_RemovesExactlyTheKeyTheRecordCarried()
    {
        // Arrange
        var bucket = BucketAnswering();
        using var host = OutboundResilienceTestHost.WithConfiguredSettings();
        var store = StoreOver(bucket, host);

        // Act
        await store.DeleteAsync("mailfathom/mailbox-exports/written", TestContext.Current.CancellationToken);

        // Assert
        await bucket.Received(1).DeleteObjectAsync(
            Arg.Is<DeleteObjectRequest>(request =>
                request != null
                && request.BucketName == "payloads"
                && request.Key == "mailfathom/mailbox-exports/written"),
            Arg.Any<CancellationToken>());
    }

    /// <summary>A locator nobody supplied is a caller's defect rather than an endpoint's answer, so it is refused before a request is made.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task DeleteAsync_ALocatorNamingNothing_IsRefusedBeforeAnyRequest(string objectLocator)
    {
        // Arrange
        var bucket = BucketAnswering();
        using var host = OutboundResilienceTestHost.WithConfiguredSettings();
        var store = StoreOver(bucket, host);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => store.DeleteAsync(objectLocator, TestContext.Current.CancellationToken));
        await bucket.DidNotReceive().DeleteObjectAsync(
            Arg.Any<DeleteObjectRequest>(),
            Arg.Any<CancellationToken>());
    }

    private static IAmazonS3 BucketAnswering()
    {
        var bucket = Substitute.For<IAmazonS3>();
        bucket.InitiateMultipartUploadAsync(
                Arg.Any<InitiateMultipartUploadRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(new InitiateMultipartUploadResponse { UploadId = "upload-1" }));
        var partsAccepted = 0;
        bucket.UploadPartAsync(Arg.Any<UploadPartRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(new UploadPartResponse
            {
                ETag = $"etag-{Interlocked.Increment(ref partsAccepted)}",
            }));
        bucket.CompleteMultipartUploadAsync(
                Arg.Any<CompleteMultipartUploadRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(new CompleteMultipartUploadResponse()));
        bucket.AbortMultipartUploadAsync(Arg.Any<AbortMultipartUploadRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(new AbortMultipartUploadResponse()));
        bucket.GetObjectAsync(Arg.Any<GetObjectRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(new GetObjectResponse
            {
                ResponseStream = new MemoryStream(Encoding.ASCII.GetBytes("archive")),
            }));
        bucket.DeleteObjectAsync(Arg.Any<DeleteObjectRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(new DeleteObjectResponse()));

        return bucket;
    }

    private static S3MailboxExportArchiveStore StoreOver(IAmazonS3 bucket, OutboundResilienceTestHost host)
    {
        var clientFactory = Substitute.For<IObjectStorageClientFactory>();
        clientFactory.Endpoint.Returns(Endpoint);
        clientFactory.OpenAsync(Arg.Any<CancellationToken>()).Returns(
            _ => Task.FromResult(new OpenedObjectStorageClient(bucket, ownedClient: null, credential: null)));

        var lifetime = Substitute.For<IHostApplicationLifetime>();
        lifetime.ApplicationStopping.Returns(CancellationToken.None);

        return new S3MailboxExportArchiveStore(
            clientFactory,
            new ObjectStorageOperationRunner(
                host.Executor,
                new ObjectStorageTelemetry(new FakeTimeProvider()),
                lifetime));
    }

    private static AmazonS3Exception Absent() => new(
        "the endpoint holds nothing under that key",
        ErrorType.Sender,
        "NoSuchKey",
        "request-id",
        HttpStatusCode.NotFound);
}
