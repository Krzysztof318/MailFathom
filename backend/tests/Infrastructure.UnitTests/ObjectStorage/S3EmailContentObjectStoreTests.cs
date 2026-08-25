// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net;
using System.Security.Cryptography;
using System.Text;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Infrastructure.ObjectStorage;
using MailFathom.Infrastructure.Observability;
using MailFathom.Infrastructure.UnitTests.TestDoubles;
using MailFathom.TestSupport;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.ObjectStorage;

/// <summary>Covers what a payload's object is named, what the write asserts about it, and what a read answers.</summary>
/// <remarks>
/// The key is the subject of most of this. It is minted per write and never derived, which is what lets the object be
/// durable before the row that points at it exists — so the claims worth proving are that two writes of one payload
/// never collide, that the write refuses to overwrite anything, and that what comes back describes the object the
/// endpoint agreed it received.
/// </remarks>
public sealed class S3EmailContentObjectStoreTests
{
    /// <summary>The step the retry budget's own backoff is walked on, which is virtual time rather than the clock.</summary>
    private static readonly TimeSpan FineAdvanceStep = TimeSpan.FromMilliseconds(100);

    private static readonly ObjectStorageEndpoint Endpoint = ObjectStorageEndpoint.Create(
        new Uri("https://objects.example.test:9000/"),
        "payloads",
        "mailfathom",
        "eu-central-1",
        usePathStyleAddressing: true,
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(100));

    private static readonly byte[] Message = "From: writer@example.test\r\n\r\nShall we?"u8.ToArray();

    /// <summary>What the row will carry has to describe the object that was just written, or a read cannot check it.</summary>
    [Fact]
    public async Task PlaceAsync_APayload_AnswersTheLocator_TheLength_AndTheDigestOfWhatItWrote()
    {
        // Arrange
        var bucket = BucketAnswering();
        using var host = OutboundResilienceTestHost.WithConfiguredSettings();
        var store = StoreOver(bucket, host);

        // Act
        var placed = await store.PlaceAsync(
            EmailContentKind.IncomingMessage,
            Message,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(ContentStorageBackend.ObjectStorage, placed.Backend);
        Assert.Equal(Message.Length, placed.ByteLength);
        Assert.Equal(SHA256.HashData(Message), placed.Sha256Hash.ToArray());
        Assert.True(placed.RawMime.IsEmpty);
        Assert.StartsWith("mailfathom/incoming/", placed.ObjectLocator!, StringComparison.Ordinal);
    }

    /// <summary>The locator the caller is handed is the key the object went to, because nothing ever recomputes one.</summary>
    [Fact]
    public async Task PlaceAsync_APayload_WritesUnderExactlyTheKeyItAnswers()
    {
        // Arrange
        var bucket = BucketAnswering();
        using var host = OutboundResilienceTestHost.WithConfiguredSettings();
        var store = StoreOver(bucket, host);

        // Act
        var placed = await store.PlaceAsync(
            EmailContentKind.MailDraft,
            Message,
            TestContext.Current.CancellationToken);

        // Assert
        await bucket.Received(1).PutObjectAsync(
            Arg.Is<PutObjectRequest>(request =>
                request != null && request.BucketName == "payloads" && request.Key == placed.ObjectLocator),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The endpoint verifies the upload against the digest and refuses a corrupted one, so a row carrying that digest
    /// describes an object the endpoint agreed it received intact rather than one this process merely hashed.
    /// </summary>
    [Fact]
    public async Task PlaceAsync_APayload_SendsTheDigestAsTheRequestsOwnChecksum()
    {
        // Arrange
        var bucket = BucketAnswering();
        using var host = OutboundResilienceTestHost.WithConfiguredSettings();
        var store = StoreOver(bucket, host);
        var expectedChecksum = Convert.ToBase64String(SHA256.HashData(Message));

        // Act
        await store.PlaceAsync(EmailContentKind.OutgoingMessage, Message, TestContext.Current.CancellationToken);

        // Assert
        await bucket.Received(1).PutObjectAsync(
            Arg.Is<PutObjectRequest>(request => request != null && request.ChecksumSHA256 == expectedChecksum),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The key was minted for this write, so an object already under it is a reused key rather than a repeat to
    /// absorb. Writing conditionally is what turns that from silent data loss into a failure somebody reads.
    /// </summary>
    [Fact]
    public async Task PlaceAsync_APayload_WritesOnlyWhereNothingIsHeldYet()
    {
        // Arrange
        var bucket = BucketAnswering();
        using var host = OutboundResilienceTestHost.WithConfiguredSettings();
        var store = StoreOver(bucket, host);

        // Act
        await store.PlaceAsync(EmailContentKind.RecurringSendDraft, Message, TestContext.Current.CancellationToken);

        // Assert
        await bucket.Received(1).PutObjectAsync(
            Arg.Is<PutObjectRequest>(request => request != null && request.IfNoneMatch == "*"),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A mail draft's revision is placed before the unit of work that would replace the row, so the two revisions must
    /// not share a key: a commit that never happens has to leave the previous revision's object intact.
    /// </summary>
    [Fact]
    public async Task PlaceAsync_TheSamePayloadTwice_MintsADifferentKeyEachTime()
    {
        // Arrange
        var bucket = BucketAnswering();
        using var host = OutboundResilienceTestHost.WithConfiguredSettings();
        var store = StoreOver(bucket, host);

        // Act
        var first = await store.PlaceAsync(
            EmailContentKind.MailDraft,
            Message,
            TestContext.Current.CancellationToken);
        var second = await store.PlaceAsync(
            EmailContentKind.MailDraft,
            Message,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.NotEqual(first.ObjectLocator, second.ObjectLocator);
        Assert.Equal(first.Sha256Hash.ToArray(), second.Sha256Hash.ToArray());
    }

    /// <summary>
    /// Under a minted key the condition can only fail if a key was reused, which nothing here may do. Absorbing that as
    /// success would hand back a locator naming somebody else's mail.
    /// </summary>
    [Fact]
    public async Task PlaceAsync_AnEndpointThatRefusesTheConditionalWrite_ReportsItRatherThanAnsweringTheLocator()
    {
        // Arrange
        var bucket = BucketAnswering();
        bucket.PutObjectAsync(Arg.Any<PutObjectRequest>(), Arg.Any<CancellationToken>())
            .Returns<Task<PutObjectResponse>>(_ =>
                throw Answered(HttpStatusCode.PreconditionFailed, "PreconditionFailed"));

        using var host = OutboundResilienceTestHost.WithConfiguredSettings(("ObjectStorageInvocation:MaxAttempts", "1"));
        var store = StoreOver(bucket, host);

        // Act, Assert
        await Assert.ThrowsAsync<ObjectStorageUnavailableException>(
            () => store.PlaceAsync(EmailContentKind.MailDraft, Message, TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// A stream read to its end uploads nothing on a repeat and reports success for it, which is the one failure a
    /// retry must not be able to produce: the row would then point at an empty object carrying a digest of the message.
    /// </summary>
    [Fact]
    public async Task PlaceAsync_AWriteThatIsRepeated_UploadsThePayloadOnEveryAttempt()
    {
        // Arrange
        var bucket = BucketAnswering();
        var uploadedLengths = new List<long>();
        var refusalsLeft = 2;
        bucket.PutObjectAsync(Arg.Any<PutObjectRequest>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            uploadedLengths.Add(call.Arg<PutObjectRequest>()?.InputStream?.Length ?? -1L);

            return refusalsLeft-- > 0
                ? throw Answered(HttpStatusCode.ServiceUnavailable, "SlowDown")
                : Task.FromResult(new PutObjectResponse());
        });

        using var host = OutboundResilienceTestHost.WithConfiguredSettings(("ObjectStorageInvocation:MaxAttempts", "3"));
        var store = StoreOver(bucket, host);

        // Act
        var placement = store.PlaceAsync(
            EmailContentKind.IncomingMessage,
            Message,
            TestContext.Current.CancellationToken);
        await host.CompleteOnVirtualTimeAsync(placement, FineAdvanceStep);

        // Assert
        Assert.Equal([Message.Length, Message.Length, Message.Length], uploadedLengths);
    }

    /// <summary>
    /// A payload that is a window onto a larger buffer uploads its own bytes and none of the buffer's. The request
    /// carries a stream rather than a memory, so the one place this could go wrong is the conversion between the two —
    /// and getting it wrong would send somebody else's mail under this message's key.
    /// </summary>
    [Fact]
    public async Task PlaceAsync_APayloadThatIsASliceOfALargerBuffer_UploadsExactlyTheSlice()
    {
        // Arrange
        var bucket = BucketAnswering();
        byte[]? uploaded = null;
        bucket.PutObjectAsync(Arg.Any<PutObjectRequest>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            using var received = new MemoryStream();
            call.Arg<PutObjectRequest>()!.InputStream.CopyTo(received);
            uploaded = received.ToArray();

            return Task.FromResult(new PutObjectResponse());
        });

        using var host = OutboundResilienceTestHost.WithConfiguredSettings();
        var store = StoreOver(bucket, host);

        byte[] surrounding = [.. Enumerable.Repeat<byte>(0xFF, 8), .. Message, .. Enumerable.Repeat<byte>(0xFF, 8)];
        var slice = new ReadOnlyMemory<byte>(surrounding, 8, Message.Length);

        // Act
        var placed = await store.PlaceAsync(
            EmailContentKind.IncomingMessage,
            slice,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(Message, uploaded);
        Assert.Equal(Message.Length, placed.ByteLength);
        Assert.Equal(SHA256.HashData(Message), placed.Sha256Hash.ToArray());
    }

    /// <summary>Every payload kind is written into a group of its own, which is what makes a listing readable.</summary>
    [Theory]
    [InlineData(EmailContentKind.IncomingMessage, "mailfathom/incoming/")]
    [InlineData(EmailContentKind.OutgoingMessage, "mailfathom/outgoing/")]
    [InlineData(EmailContentKind.RecurringSendDraft, "mailfathom/recurring-send-drafts/")]
    [InlineData(EmailContentKind.MailDraft, "mailfathom/mail-drafts/")]
    public async Task PlaceAsync_EachPayloadKind_IsWrittenUnderItsOwnGroupOfKeys(
        EmailContentKind kind,
        string expectedPrefix)
    {
        // Arrange
        var bucket = BucketAnswering();
        using var host = OutboundResilienceTestHost.WithConfiguredSettings();
        var store = StoreOver(bucket, host);

        // Act
        var placed = await store.PlaceAsync(kind, Message, TestContext.Current.CancellationToken);

        // Assert
        Assert.StartsWith(expectedPrefix, placed.ObjectLocator!, StringComparison.Ordinal);
    }

    /// <summary>A payload nothing was read from is a caller's mistake rather than an object worth writing.</summary>
    [Fact]
    public async Task PlaceAsync_AnEmptyPayload_IsRefusedWithoutReachingTheEndpoint()
    {
        // Arrange
        var bucket = BucketAnswering();
        using var host = OutboundResilienceTestHost.WithConfiguredSettings();
        var store = StoreOver(bucket, host);

        // Act, Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => store.PlaceAsync(
                EmailContentKind.IncomingMessage,
                ReadOnlyMemory<byte>.Empty,
                TestContext.Current.CancellationToken));
        await bucket.DidNotReceive().PutObjectAsync(Arg.Any<PutObjectRequest>(), Arg.Any<CancellationToken>());
    }

    /// <summary>A read answers the bytes the endpoint holds, which is what the recorded length and digest are checked against above it.</summary>
    [Fact]
    public async Task FindAsync_AnObjectTheEndpointHolds_AnswersItsBytes()
    {
        // Arrange
        var bucket = BucketAnswering();
        bucket.GetObjectAsync(Arg.Any<GetObjectRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(new GetObjectResponse { ResponseStream = new MemoryStream(Message) }));

        using var host = OutboundResilienceTestHost.WithConfiguredSettings();
        var store = StoreOver(bucket, host);

        // Act
        var payload = await store.FindAsync("mailfathom/incoming/one", Message.Length, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(payload);
        Assert.Equal(Message, payload.Value.ToArray());
    }

    /// <summary>
    /// An endpoint answering with more than the row records is bounded here rather than buffered whole: the read stops
    /// one byte past the ceiling, which is a length no row describes and every reader already holds the digest to
    /// refuse.
    /// </summary>
    [Fact]
    public async Task FindAsync_AnObjectLongerThanTheRowRecords_ReadsNoFurtherThanOneBytePastTheCeiling()
    {
        // Arrange
        var overlongAnswer = new byte[64 * 1024];
        var bucket = BucketAnswering();
        bucket.GetObjectAsync(Arg.Any<GetObjectRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(new GetObjectResponse { ResponseStream = new MemoryStream(overlongAnswer) }));

        using var host = OutboundResilienceTestHost.WithConfiguredSettings();
        var store = StoreOver(bucket, host);

        // Act
        var payload = await store.FindAsync(
            "mailfathom/incoming/one",
            Message.Length,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(Message.Length + 1, payload?.Length);
    }

    /// <summary>
    /// An absent object is an answer rather than a failure, so it reaches the reader as the same content-unavailable
    /// outcome a missing database payload produces — and it is not retried, because a key nothing holds will not be
    /// held by the attempt after it.
    /// </summary>
    [Fact]
    public async Task FindAsync_AKeyTheEndpointHoldsNothingUnder_AnswersNothingAndIsNotAskedAgain()
    {
        // Arrange
        var bucket = BucketAnswering();
        bucket.GetObjectAsync(Arg.Any<GetObjectRequest>(), Arg.Any<CancellationToken>())
            .Returns<Task<GetObjectResponse>>(_ => throw new AmazonS3Exception("no such key")
            {
                StatusCode = HttpStatusCode.NotFound,
            });

        using var host = OutboundResilienceTestHost.WithConfiguredSettings(("ObjectStorageInvocation:MaxAttempts", "3"));
        var store = StoreOver(bucket, host);

        // Act
        var payload = await store.FindAsync("mailfathom/incoming/gone", Message.Length, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(payload);
        await bucket.Received(1).GetObjectAsync(Arg.Any<GetObjectRequest>(), Arg.Any<CancellationToken>());
    }

    /// <summary>An endpoint that could not be reached is a failure rather than an absence, or lost mail would read as mail nobody stored.</summary>
    [Fact]
    public async Task FindAsync_AnEndpointThatCannotBeReached_ReportsTheFailureRatherThanAnAbsence()
    {
        // Arrange
        var bucket = BucketAnswering();
        bucket.GetObjectAsync(Arg.Any<GetObjectRequest>(), Arg.Any<CancellationToken>())
            .Returns<Task<GetObjectResponse>>(_ => throw new HttpRequestException("no route to host"));

        using var host = OutboundResilienceTestHost.WithConfiguredSettings(("ObjectStorageInvocation:MaxAttempts", "1"));
        var store = StoreOver(bucket, host);

        // Act
        var failure = await Assert.ThrowsAsync<ObjectStorageUnavailableException>(
            () => store.FindAsync("mailfathom/incoming/one", Message.Length, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(ObjectStorageFailure.TransientTransportFailure, failure.Failure);
    }

    /// <summary>A key names one message, so it is personal data and stays out of what a failure publishes.</summary>
    [Fact]
    public async Task FindAsync_AFailure_NamesTheConfigurationKeyRatherThanTheObjectItWasReading()
    {
        // Arrange
        var bucket = BucketAnswering();
        bucket.GetObjectAsync(Arg.Any<GetObjectRequest>(), Arg.Any<CancellationToken>())
            .Returns<Task<GetObjectResponse>>(_ => throw new HttpRequestException("no route to host"));

        using var host = OutboundResilienceTestHost.WithConfiguredSettings(("ObjectStorageInvocation:MaxAttempts", "1"));
        var store = StoreOver(bucket, host);

        // Act
        var failure = await Assert.ThrowsAsync<ObjectStorageUnavailableException>(
            () => store.FindAsync("mailfathom/incoming/private-key-name", Message.Length, TestContext.Current.CancellationToken));

        // Assert
        Assert.Contains("ContentStorage:ObjectStorage:Endpoint", failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("private-key-name", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>A locator nothing supplied is a defect in the row that carried it rather than a read to attempt.</summary>
    [Fact]
    public async Task FindAsync_ALocatorThatNamesNothing_IsRefusedWithoutReachingTheEndpoint()
    {
        // Arrange
        var bucket = BucketAnswering();
        using var host = OutboundResilienceTestHost.WithConfiguredSettings();
        var store = StoreOver(bucket, host);

        // Act, Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => store.FindAsync("   ", Message.Length, TestContext.Current.CancellationToken));
        await bucket.DidNotReceive().GetObjectAsync(Arg.Any<GetObjectRequest>(), Arg.Any<CancellationToken>());
    }

    /// <summary>The volume a write moved is what an operator reads to size a bucket, and it is measured against the write.</summary>
    [Fact]
    public async Task PlaceAsync_APayload_PublishesWhatItWroteAgainstThatOperation()
    {
        // Arrange
        var bucket = BucketAnswering();
        using var host = OutboundResilienceTestHost.WithConfiguredSettings();
        var store = StoreOver(bucket, host, new ObjectStorageTelemetry(new FakeTimeProvider()));

        using var measurements = new RecordedMailFathomMeasurements("mailfathom.object_storage.bytes");

        // Act
        await store.PlaceAsync(EmailContentKind.IncomingMessage, Message, TestContext.Current.CancellationToken);

        // Assert
        var written = measurements.Read("mailfathom.object_storage.bytes")
            .Where(measurement =>
                measurement.Tags.GetValueOrDefault("mailfathom.object_storage.operation") as string == "put")
            .ToArray();

        Assert.NotEmpty(written);
        Assert.All(written, measurement => Assert.Equal(Message.Length, measurement.Value));
    }

    /// <summary>Removing an object is what carries a committed erasure of its row through to the endpoint.</summary>
    [Fact]
    public async Task DeleteAsync_AnObject_RemovesExactlyThatKeyFromTheConfiguredBucket()
    {
        // Arrange
        var bucket = BucketAnswering();
        using var host = OutboundResilienceTestHost.WithConfiguredSettings();
        var store = StoreOver(bucket, host);

        // Act
        await store.DeleteAsync("mailfathom/incoming/one", TestContext.Current.CancellationToken);

        // Assert
        await bucket.Received(1).DeleteObjectAsync(
            Arg.Is<DeleteObjectRequest>(request =>
                request != null && request.BucketName == "payloads" && request.Key == "mailfathom/incoming/one"),
            Arg.Any<CancellationToken>());
    }

    /// <summary>A locator nothing supplied is a defect in the row that carried it rather than a removal to attempt.</summary>
    [Fact]
    public async Task DeleteAsync_ALocatorThatNamesNothing_IsRefusedWithoutReachingTheEndpoint()
    {
        // Arrange
        var bucket = BucketAnswering();
        using var host = OutboundResilienceTestHost.WithConfiguredSettings();
        var store = StoreOver(bucket, host);

        // Act, Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => store.DeleteAsync("   ", TestContext.Current.CancellationToken));
        await bucket.DidNotReceive().DeleteObjectAsync(
            Arg.Any<DeleteObjectRequest>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>An endpoint that could not be reached is reported, because the object it still holds is mail nothing points at.</summary>
    [Fact]
    public async Task DeleteAsync_AnEndpointThatCannotBeReached_ReportsTheFailure()
    {
        // Arrange
        var bucket = BucketAnswering();
        bucket.DeleteObjectAsync(Arg.Any<DeleteObjectRequest>(), Arg.Any<CancellationToken>())
            .Returns<Task<DeleteObjectResponse>>(_ => throw new HttpRequestException("no route to host"));

        using var host = OutboundResilienceTestHost.WithConfiguredSettings(("ObjectStorageInvocation:MaxAttempts", "1"));
        var store = StoreOver(bucket, host);

        // Act
        var failure = await Assert.ThrowsAsync<ObjectStorageUnavailableException>(
            () => store.DeleteAsync("mailfathom/incoming/one", TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(ObjectStorageFailure.TransientTransportFailure, failure.Failure);
    }

    /// <summary>
    /// A sweep may delete only what this deployment wrote, and a prefix is the whole of what separates two deployments
    /// sharing one bucket. A listing that reached outside it would let reclamation remove somebody else's mail.
    /// </summary>
    [Fact]
    public async Task ListAsync_APage_AsksOnlyForKeysBeneathTheConfiguredPrefix()
    {
        // Arrange
        var bucket = BucketAnswering();
        using var host = OutboundResilienceTestHost.WithConfiguredSettings();
        var store = StoreOver(bucket, host);

        // Act
        await store.ListAsync(continuationToken: null, maxObjects: 1000, TestContext.Current.CancellationToken);

        // Assert
        await bucket.Received(1).ListObjectsV2Async(
            Arg.Is<ListObjectsV2Request>(request =>
                request != null
                && request.BucketName == "payloads"
                && request.Prefix == "mailfathom/"
                && request.MaxKeys == 1000),
            Arg.Any<CancellationToken>());
    }

    /// <summary>What a sweep decides about an object is its key, its age, and its size, so all three come back with it.</summary>
    [Fact]
    public async Task ListAsync_APage_AnswersEachObjectWithWhatTheSweepDecidesAboutIt()
    {
        // Arrange
        var writtenAt = new DateTime(2026, 8, 24, 9, 30, 0, DateTimeKind.Utc);
        var bucket = BucketAnswering();
        bucket.ListObjectsV2Async(Arg.Any<ListObjectsV2Request>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(new ListObjectsV2Response
            {
                S3Objects =
                [
                    new S3Object { Key = "mailfathom/incoming/one", LastModified = writtenAt, Size = 4096 },
                ],
                IsTruncated = false,
            }));

        using var host = OutboundResilienceTestHost.WithConfiguredSettings();
        var store = StoreOver(bucket, host);

        // Act
        var page = await store.ListAsync(
            continuationToken: null,
            maxObjects: 1000,
            TestContext.Current.CancellationToken);

        // Assert
        var listed = Assert.Single(page.Objects);
        Assert.Equal("mailfathom/incoming/one", listed.Key);
        Assert.Equal(new DateTimeOffset(writtenAt), listed.WrittenAt);
        Assert.Equal(4096, listed.ByteLength);
        Assert.Null(page.ContinuationToken);
    }

    /// <summary>An endpoint that named no moment is reported as having named none, rather than as an object of some age.</summary>
    /// <remarks>
    /// The moment decides whether the sweep may touch the object at all, so inventing one here would decide the age
    /// floor from a value the endpoint never stated. The absence is carried through and the sweep leaves such an object
    /// alone.
    /// </remarks>
    [Fact]
    public async Task ListAsync_AnObjectTheEndpointNamedNoMomentFor_AnswersWithNoMoment()
    {
        // Arrange
        var bucket = BucketAnswering();
        bucket.ListObjectsV2Async(Arg.Any<ListObjectsV2Request>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(new ListObjectsV2Response
            {
                S3Objects = [new S3Object { Key = "mailfathom/incoming/undated", Size = 4096 }],
                IsTruncated = false,
            }));

        using var host = OutboundResilienceTestHost.WithConfiguredSettings();
        var store = StoreOver(bucket, host);

        // Act
        var page = await store.ListAsync(
            continuationToken: null,
            maxObjects: 1000,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(Assert.Single(page.Objects).WrittenAt);
    }

    /// <summary>
    /// The listing's own flag decides whether there is another page, not whether a token came back. An endpoint that
    /// ended the listing and echoed a token anyway would otherwise be listed from that token for ever.
    /// </summary>
    [Fact]
    public async Task ListAsync_AnEndedListingThatStillEchoesAToken_AnswersNoContinuation()
    {
        // Arrange
        var bucket = BucketAnswering();
        bucket.ListObjectsV2Async(Arg.Any<ListObjectsV2Request>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(new ListObjectsV2Response
            {
                S3Objects = [],
                IsTruncated = false,
                NextContinuationToken = "echoed",
            }));

        using var host = OutboundResilienceTestHost.WithConfiguredSettings();
        var store = StoreOver(bucket, host);

        // Act
        var page = await store.ListAsync(
            continuationToken: null,
            maxObjects: 1000,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(page.ContinuationToken);
        Assert.Empty(page.Objects);
    }

    /// <summary>A truncated listing hands back the token the next page is asked for with, which is what makes a sweep resumable.</summary>
    [Fact]
    public async Task ListAsync_ATruncatedListing_AnswersTheTokenTheNextPageIsAskedForWith()
    {
        // Arrange
        var bucket = BucketAnswering();
        bucket.ListObjectsV2Async(Arg.Any<ListObjectsV2Request>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(new ListObjectsV2Response
            {
                S3Objects = [],
                IsTruncated = true,
                NextContinuationToken = "next-page",
            }));

        using var host = OutboundResilienceTestHost.WithConfiguredSettings();
        var store = StoreOver(bucket, host);

        // Act
        var page = await store.ListAsync(
            continuationToken: "this-page",
            maxObjects: 1000,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("next-page", page.ContinuationToken);
        await bucket.Received(1).ListObjectsV2Async(
            Arg.Is<ListObjectsV2Request>(request => request != null && request.ContinuationToken == "this-page"),
            Arg.Any<CancellationToken>());
    }

    /// <summary>The SDK leaves an absent collection null from version 4 onwards, which is what an empty prefix answers with.</summary>
    [Fact]
    public async Task ListAsync_AnEndpointHoldingNothingBeneathThePrefix_AnswersAnEmptyPage()
    {
        // Arrange
        var bucket = BucketAnswering();
        bucket.ListObjectsV2Async(Arg.Any<ListObjectsV2Request>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(new ListObjectsV2Response { S3Objects = null, IsTruncated = false }));

        using var host = OutboundResilienceTestHost.WithConfiguredSettings();
        var store = StoreOver(bucket, host);

        // Act
        var page = await store.ListAsync(
            continuationToken: null,
            maxObjects: 1000,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(page.Objects);
        Assert.Null(page.ContinuationToken);
    }

    [Fact]
    public void Construction_MissingCollaborator_IsRefused()
    {
        // Arrange
        using var host = OutboundResilienceTestHost.WithConfiguredSettings();
        var clientFactory = Substitute.For<IObjectStorageClientFactory>();
        var operationRunner = new ObjectStorageOperationRunner(
            host.Executor,
            new ObjectStorageTelemetry(new FakeTimeProvider()),
            Substitute.For<IHostApplicationLifetime>());

        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => new S3EmailContentObjectStore(null!, operationRunner));
        Assert.Throws<ArgumentNullException>(() => new S3EmailContentObjectStore(clientFactory, null!));
    }

    private static IAmazonS3 BucketAnswering()
    {
        var bucket = Substitute.For<IAmazonS3>();
        bucket.PutObjectAsync(Arg.Any<PutObjectRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(new PutObjectResponse()));
        bucket.GetObjectAsync(Arg.Any<GetObjectRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(new GetObjectResponse
            {
                ResponseStream = new MemoryStream(Encoding.ASCII.GetBytes("stored")),
            }));
        bucket.DeleteObjectAsync(Arg.Any<DeleteObjectRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(new DeleteObjectResponse()));
        bucket.ListObjectsV2Async(Arg.Any<ListObjectsV2Request>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(new ListObjectsV2Response { S3Objects = [], IsTruncated = false }));

        return bucket;
    }

    private static S3EmailContentObjectStore StoreOver(
        IAmazonS3 bucket,
        OutboundResilienceTestHost host,
        ObjectStorageTelemetry? telemetry = null)
    {
        var clientFactory = Substitute.For<IObjectStorageClientFactory>();
        clientFactory.Endpoint.Returns(Endpoint);
        clientFactory.OpenAsync(Arg.Any<CancellationToken>()).Returns(
            _ => Task.FromResult(new OpenedObjectStorageClient(bucket, ownedClient: null, credential: null)));

        var lifetime = Substitute.For<IHostApplicationLifetime>();
        lifetime.ApplicationStopping.Returns(CancellationToken.None);

        return new S3EmailContentObjectStore(
            clientFactory,
            new ObjectStorageOperationRunner(
                host.Executor,
                telemetry ?? new ObjectStorageTelemetry(new FakeTimeProvider()),
                lifetime));
    }

    private static AmazonServiceException Answered(HttpStatusCode status, string errorCode) => new(
        "the endpoint answered",
        ErrorType.Sender,
        errorCode,
        "request-id",
        status);
}
