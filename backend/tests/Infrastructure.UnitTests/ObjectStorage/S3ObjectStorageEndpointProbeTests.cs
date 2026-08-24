// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using MailFathom.Domain.Failures;
using MailFathom.Infrastructure.ObjectStorage;
using MailFathom.Infrastructure.Observability;
using MailFathom.Infrastructure.UnitTests.TestDoubles;
using MailFathom.TestSupport;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.ObjectStorage;

/// <summary>Covers what the readiness probe asks the bucket, in which order, and what it stores to find out.</summary>
public sealed class S3ObjectStorageEndpointProbeTests
{
    private const string ProbeKey = "mailfathom/.mailfathom/readiness-probe";

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

    /// <summary>
    /// An endpoint that answers a listing and refuses a write is a deployment that will accept mail and be unable to
    /// store it, so all three are asked — and the removal keeps a bucket from accumulating what the probe wrote.
    /// </summary>
    [Fact]
    public async Task VerifyAvailableAsync_ABucketThatAnswers_Lists_Writes_AndRemovesWhatItWrote()
    {
        // Arrange
        var bucket = BucketAnswering();
        using var host = OutboundResilienceTestHost.WithConfiguredSettings();
        var probe = ProbeOver(bucket, host);

        // Act
        await probe.VerifyAvailableAsync(TestContext.Current.CancellationToken);

        // Assert
        await bucket.Received(1).ListObjectsV2Async(
            Arg.Is<ListObjectsV2Request>(request => request != null
                && request.BucketName == "payloads"
                && request.Prefix == "mailfathom/"
                && request.MaxKeys == 1),
            Arg.Any<CancellationToken>());
        await bucket.Received(1).PutObjectAsync(
            Arg.Is<PutObjectRequest>(request =>
                request != null && request.BucketName == "payloads" && request.Key == ProbeKey),
            Arg.Any<CancellationToken>());
        await bucket.Received(1).DeleteObjectAsync(
            Arg.Is<DeleteObjectRequest>(request =>
                request != null && request.BucketName == "payloads" && request.Key == ProbeKey),
            Arg.Any<CancellationToken>());
    }

    /// <summary>Nothing about a message reaches the bucket to establish that the bucket works.</summary>
    [Fact]
    public async Task VerifyAvailableAsync_TheObjectItWrites_CarriesNoPayload()
    {
        // Arrange
        var bucket = BucketAnswering();
        using var host = OutboundResilienceTestHost.WithConfiguredSettings();
        var probe = ProbeOver(bucket, host);
        var writtenLengths = new List<long>();
        bucket.PutObjectAsync(Arg.Any<PutObjectRequest>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            writtenLengths.Add(call.Arg<PutObjectRequest>()?.InputStream?.Length ?? -1L);

            return Task.FromResult(new PutObjectResponse());
        });

        // Act
        await probe.VerifyAvailableAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([0L], writtenLengths);
    }

    /// <summary>
    /// Each of the three is a precondition of the next being meaningful, so the probe stops at the first that fails.
    /// Reporting three failures for one unreachable endpoint would tell an operator that three things are wrong.
    /// </summary>
    [Fact]
    public async Task VerifyAvailableAsync_ABucketThatCannotBeListed_AsksNothingFurther()
    {
        // Arrange
        var bucket = BucketAnswering();
        bucket.ListObjectsV2Async(Arg.Any<ListObjectsV2Request>(), Arg.Any<CancellationToken>())
            .Returns<Task<ListObjectsV2Response>>(_ => throw Answered(HttpStatusCode.Forbidden, "AccessDenied"));

        using var host = OutboundResilienceTestHost.WithConfiguredSettings(("ObjectStorageInvocation:MaxAttempts", "1"));
        var probe = ProbeOver(bucket, host);

        // Act
        await Assert.ThrowsAsync<ObjectStorageUnavailableException>(
            () => probe.VerifyAvailableAsync(TestContext.Current.CancellationToken));

        // Assert
        await bucket.DidNotReceive().PutObjectAsync(Arg.Any<PutObjectRequest>(), Arg.Any<CancellationToken>());
        await bucket.DidNotReceive().DeleteObjectAsync(Arg.Any<DeleteObjectRequest>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The endpoint's own answer decides whether an attempt is repeated, so an overloaded endpoint gets the whole
    /// configured budget and a refused credential gets one attempt. This is what the translation inside the attempt
    /// buys: handed the AWS client's own type, the pipeline would judge both by the transport rules, match neither, and
    /// give a transient blip a single attempt.
    /// </summary>
    [Fact]
    public async Task VerifyAvailableAsync_AnEndpointThatIsOverloaded_IsAskedAgainForTheWholeConfiguredBudget()
    {
        // Arrange
        var overloadedBucket = BucketAnswering();
        overloadedBucket.ListObjectsV2Async(Arg.Any<ListObjectsV2Request>(), Arg.Any<CancellationToken>())
            .Returns<Task<ListObjectsV2Response>>(_ => throw Answered(HttpStatusCode.ServiceUnavailable, "SlowDown"));

        var refusingBucket = BucketAnswering();
        refusingBucket.ListObjectsV2Async(Arg.Any<ListObjectsV2Request>(), Arg.Any<CancellationToken>())
            .Returns<Task<ListObjectsV2Response>>(_ => throw Answered(HttpStatusCode.Forbidden, "SignatureDoesNotMatch"));

        using var host = OutboundResilienceTestHost.WithConfiguredSettings(
            ("ObjectStorageInvocation:MaxAttempts", "3"));

        // Act
        var overloadedScrape = ProbeOver(overloadedBucket, host)
            .VerifyAvailableAsync(TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<ObjectStorageUnavailableException>(
            () => host.CompleteOnVirtualTimeAsync(overloadedScrape, FineAdvanceStep));

        var refusedScrape = ProbeOver(refusingBucket, host)
            .VerifyAvailableAsync(TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<ObjectStorageUnavailableException>(
            () => host.CompleteOnVirtualTimeAsync(refusedScrape, FineAdvanceStep));

        // Assert
        await overloadedBucket.Received(3).ListObjectsV2Async(
            Arg.Any<ListObjectsV2Request>(),
            Arg.Any<CancellationToken>());
        await refusingBucket.Received(1).ListObjectsV2Async(
            Arg.Any<ListObjectsV2Request>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>A bucket that lists and refuses a write is exactly the state worth finding before the first message needs it.</summary>
    [Fact]
    public async Task VerifyAvailableAsync_ABucketThatRefusesAWrite_ReportsTheRefusalUnderItsOwnCode()
    {
        // Arrange
        var bucket = BucketAnswering();
        bucket.PutObjectAsync(Arg.Any<PutObjectRequest>(), Arg.Any<CancellationToken>())
            .Returns<Task<PutObjectResponse>>(_ => throw Answered(HttpStatusCode.Forbidden, "AccessDenied"));

        using var host = OutboundResilienceTestHost.WithConfiguredSettings(("ObjectStorageInvocation:MaxAttempts", "1"));
        var probe = ProbeOver(bucket, host);

        // Act
        var failure = await Assert.ThrowsAsync<ObjectStorageUnavailableException>(
            () => probe.VerifyAvailableAsync(TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(ObjectStorageFailure.AuthenticationFailed, failure.Failure);
        Assert.Equal(MailFathomErrorCode.ObjectStorageAuthenticationFailed, failure.ErrorCode);
    }

    /// <summary>An endpoint that could not be reached is a different act for an operator than one that refused a credential.</summary>
    [Fact]
    public async Task VerifyAvailableAsync_AnEndpointThatCannotBeReached_ReportsItAsTransport()
    {
        // Arrange
        var bucket = BucketAnswering();
        bucket.ListObjectsV2Async(Arg.Any<ListObjectsV2Request>(), Arg.Any<CancellationToken>())
            .Returns<Task<ListObjectsV2Response>>(_ => throw new HttpRequestException("no route to host"));

        using var host = OutboundResilienceTestHost.WithConfiguredSettings(("ObjectStorageInvocation:MaxAttempts", "1"));
        var probe = ProbeOver(bucket, host);

        // Act
        var failure = await Assert.ThrowsAsync<ObjectStorageUnavailableException>(
            () => probe.VerifyAvailableAsync(TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(ObjectStorageFailure.TransientTransportFailure, failure.Failure);
        Assert.Equal(MailFathomErrorCode.ObjectStorageEndpointUnavailable, failure.ErrorCode);
    }

    /// <summary>
    /// A scrape the caller abandoned stays what it was: a fact about the caller. Translating it would report a bucket
    /// that failed and take an instance out of traffic over a request nobody was waiting for.
    /// </summary>
    [Fact]
    public async Task VerifyAvailableAsync_AProbeTheCallerAbandoned_PropagatesRatherThanReportingTheBucket()
    {
        // Arrange
        using var caller = new CancellationTokenSource();
        var bucket = BucketAnswering();
        bucket.ListObjectsV2Async(Arg.Any<ListObjectsV2Request>(), Arg.Any<CancellationToken>())
            .Returns<Task<ListObjectsV2Response>>(_ =>
            {
                caller.Cancel();

                throw new OperationCanceledException(caller.Token);
            });

        using var host = OutboundResilienceTestHost.WithConfiguredSettings(("ObjectStorageInvocation:MaxAttempts", "1"));
        var probe = ProbeOver(bucket, host);

        // Act, Assert
        await Assert.ThrowsAsync<OperationCanceledException>(() => probe.VerifyAvailableAsync(caller.Token));
    }

    /// <summary>A shutdown is not an endpoint that failed, and it carries a code of its own so an alert does not fire on a stop.</summary>
    [Fact]
    public async Task VerifyAvailableAsync_AProbeTheHostsShutdownEnded_ReportsItAsAShutdown()
    {
        // Arrange
        using var shutdown = new CancellationTokenSource();
        await shutdown.CancelAsync();

        var bucket = BucketAnswering();
        bucket.ListObjectsV2Async(Arg.Any<ListObjectsV2Request>(), Arg.Any<CancellationToken>())
            .Returns<Task<ListObjectsV2Response>>(_ => throw new OperationCanceledException());

        using var host = OutboundResilienceTestHost.WithConfiguredSettings(("ObjectStorageInvocation:MaxAttempts", "1"));
        var probe = ProbeOver(bucket, host, shutdown: shutdown);

        // Act
        var failure = await Assert.ThrowsAsync<ObjectStorageUnavailableException>(
            () => probe.VerifyAvailableAsync(TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(ObjectStorageFailure.HostShuttingDown, failure.Failure);
    }

    /// <summary>The endpoint's own answer is diagnostic detail for a log rather than something a boundary republishes.</summary>
    [Fact]
    public async Task VerifyAvailableAsync_AFailure_NamesTheConfigurationKeyRatherThanTheAddressOrTheKey()
    {
        // Arrange
        var bucket = BucketAnswering();
        bucket.ListObjectsV2Async(Arg.Any<ListObjectsV2Request>(), Arg.Any<CancellationToken>())
            .Returns<Task<ListObjectsV2Response>>(_ =>
                throw new HttpRequestException("no route to objects.example.test:9000"));

        using var host = OutboundResilienceTestHost.WithConfiguredSettings(("ObjectStorageInvocation:MaxAttempts", "1"));
        var probe = ProbeOver(bucket, host);

        // Act
        var failure = await Assert.ThrowsAsync<ObjectStorageUnavailableException>(
            () => probe.VerifyAvailableAsync(TestContext.Current.CancellationToken));

        // Assert
        Assert.Contains("ContentStorage:ObjectStorage:Endpoint", failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("objects.example.test", failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("payloads", failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(ProbeKey, failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The one measurement a probe adds beside its verdict: how the operation ended, split by what ended it, which is
    /// what an operator reads to tell a refused credential from an endpoint that has gone away.
    /// </summary>
    [Fact]
    public async Task VerifyAvailableAsync_ABucketThatRefusesAWrite_PublishesTheClassificationAgainstThatOperation()
    {
        // Arrange
        var bucket = BucketAnswering();
        bucket.PutObjectAsync(Arg.Any<PutObjectRequest>(), Arg.Any<CancellationToken>())
            .Returns<Task<PutObjectResponse>>(_ => throw Answered(HttpStatusCode.Forbidden, "AccessDenied"));

        using var host = OutboundResilienceTestHost.WithConfiguredSettings(("ObjectStorageInvocation:MaxAttempts", "1"));
        var telemetry = new ObjectStorageTelemetry(new FakeTimeProvider());
        var probe = ProbeOver(bucket, host, telemetry);

        using var measurements = new RecordedMailFathomMeasurements("mailfathom.object_storage.operations");

        // Act
        await Assert.ThrowsAsync<ObjectStorageUnavailableException>(
            () => probe.VerifyAvailableAsync(TestContext.Current.CancellationToken));

        // Assert
        var refusals = measurements.Read("mailfathom.object_storage.operations")
            .Where(measurement =>
                measurement.Tags.GetValueOrDefault("mailfathom.object_storage.operation") as string == "put"
                && measurement.Tags.GetValueOrDefault("mailfathom.object_storage.failure") as string
                    == "authentication_failed")
            .ToArray();

        Assert.NotEmpty(refusals);
        Assert.All(
            refusals,
            refusal => Assert.Equal(
                "failed",
                refusal.Tags.GetValueOrDefault("mailfathom.object_storage.outcome")));
    }

    [Fact]
    public void Construction_MissingCollaborator_IsRefused()
    {
        // Arrange
        using var host = OutboundResilienceTestHost.WithConfiguredSettings();
        var clientFactory = Substitute.For<IObjectStorageClientFactory>();
        var telemetry = new ObjectStorageTelemetry(new FakeTimeProvider());
        var lifetime = Substitute.For<IHostApplicationLifetime>();

        var operationRunner = new ObjectStorageOperationRunner(host.Executor, telemetry, lifetime);

        // Act, Assert
        Assert.Throws<ArgumentNullException>(
            () => new S3ObjectStorageEndpointProbe(null!, operationRunner));
        Assert.Throws<ArgumentNullException>(
            () => new S3ObjectStorageEndpointProbe(clientFactory, null!));
        Assert.Throws<ArgumentNullException>(
            () => new ObjectStorageOperationRunner(null!, telemetry, lifetime));
        Assert.Throws<ArgumentNullException>(
            () => new ObjectStorageOperationRunner(host.Executor, null!, lifetime));
        Assert.Throws<ArgumentNullException>(
            () => new ObjectStorageOperationRunner(host.Executor, telemetry, null!));
    }

    private static IAmazonS3 BucketAnswering()
    {
        var bucket = Substitute.For<IAmazonS3>();
        bucket.ListObjectsV2Async(Arg.Any<ListObjectsV2Request>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(new ListObjectsV2Response()));
        bucket.PutObjectAsync(Arg.Any<PutObjectRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(new PutObjectResponse()));
        bucket.DeleteObjectAsync(Arg.Any<DeleteObjectRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(new DeleteObjectResponse()));

        return bucket;
    }

    private static S3ObjectStorageEndpointProbe ProbeOver(
        IAmazonS3 bucket,
        OutboundResilienceTestHost host,
        ObjectStorageTelemetry? telemetry = null,
        CancellationTokenSource? shutdown = null)
    {
        var clientFactory = Substitute.For<IObjectStorageClientFactory>();
        clientFactory.Endpoint.Returns(Endpoint);
        clientFactory.OpenAsync(Arg.Any<CancellationToken>()).Returns(
            _ => Task.FromResult(new OpenedObjectStorageClient(bucket, ownedClient: null, credential: null)));

        var lifetime = Substitute.For<IHostApplicationLifetime>();
        lifetime.ApplicationStopping.Returns(shutdown?.Token ?? CancellationToken.None);

        return new S3ObjectStorageEndpointProbe(
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
