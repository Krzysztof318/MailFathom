// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net;
using System.Security.Cryptography;
using System.Text;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using MailFathom.AppHost;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Infrastructure.ObjectStorage;
using MailFathom.IntegrationTests.Orchestration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MailFathom.IntegrationTests.ObjectStorage;

/// <summary>Exercises every request the object backend makes against a real S3 implementation.</summary>
/// <remarks>
/// <para>
/// The class beside this one, <c>OrchestratedObjectBackedContentStoreTests</c>, runs the content store's contract over
/// this backend and asks what a caller of the port observes. This one asks the question underneath it: whether the
/// requests MailFathom composes are requests an S3 server accepts, and whether the answers it relies on are the answers
/// one gives. A substituted client cannot settle either — it accepts what it was written to accept, so a request a real
/// server rejects passes there and fails in somebody's deployment.
/// </para>
/// <para>
/// <see cref="S3Surface" /> is the list of what those requests are, and every test here is named by an entry in it.
/// That is the point of the pairing: somebody judging a second S3 implementation reads the list rather than the adapter,
/// and the list cannot drift away from what is proved.
/// </para>
/// <para>
/// Three of the behaviours are reached through the endpoint's own client rather than through
/// <see cref="IEmailContentObjectStore" />, because the port deliberately makes them unreachable: it mints a key per
/// write, so no caller can ask it to write twice under one key or to hand the endpoint a digest the payload does not
/// match. The client comes from the same factory the adapter opens, so what those tests sign, address, and send is what
/// a deployment sends.
/// </para>
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedS3SurfaceTests(MailFathomOrchestrationFixture orchestration)
{
    /// <summary>How many objects this class's paging test writes, and how many pages it asks for them in.</summary>
    /// <remarks>
    /// Small numbers on purpose: what the test is about is that a truncated page hands back a token the next request
    /// continues from, which two objects a page settles as completely as two hundred would while costing the suite
    /// nothing.
    /// </remarks>
    private const int PagedObjectCount = 5;

    private const int PageSize = 2;

    /// <summary>The ceiling a read of a key nothing holds is bounded by, which nothing ever reads a byte against.</summary>
    /// <remarks>
    /// The port takes the length the reading row records, and a read that finds no object never gets as far as reading
    /// one. Any positive value states that without pretending to describe a payload.
    /// </remarks>
    private const long UnreadCeiling = 1024;

    /// <summary>The bucket every request here is made against, which is the one the fixture created.</summary>
    private static string Bucket => OrchestrationContract.ObjectStorageBucket;

    /// <summary>
    /// The whole of a placement in one claim, for each of the four payload kinds: the endpoint holds the payload when
    /// the write answers, the key is composed under this deployment's prefix, and the kind reaches the key as a segment
    /// of its own so an operator reading a listing can tell what a group of objects is.
    /// </summary>
    [Fact]
    public async Task PlaceAsync_ForOnePayloadOfEachKind_WritesItUnderTheKindsOwnPrefixedKey()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await this.StartAsync(cancellationToken);

        (EmailContentKind Kind, string Segment)[] kinds =
        [
            (EmailContentKind.IncomingMessage, "incoming"),
            (EmailContentKind.OutgoingMessage, "outgoing"),
            (EmailContentKind.RecurringSendDraft, "recurring-send-drafts"),
            (EmailContentKind.MailDraft, "mail-drafts"),
        ];

        foreach (var (kind, segment) in kinds)
        {
            var payload = PayloadOf($"place-{segment}");

            // Act
            var placement = await services.InScopeAsync(
                (scope, token) => scope
                    .GetRequiredService<IEmailContentObjectStore>()
                    .PlaceAsync(kind, payload, token),
                cancellationToken);

            // Assert
            Assert.Equal(ContentStorageBackend.ObjectStorage, placement.Backend);
            Assert.NotNull(placement.ObjectLocator);
            Assert.StartsWith(
                $"{OrchestrationContract.ObjectStorageKeyPrefix}/{segment}/",
                placement.ObjectLocator,
                StringComparison.Ordinal);
            Assert.Equal(payload.Length, placement.ByteLength);
            Assert.Equal(SHA256.HashData(payload.Span), placement.Sha256Hash.ToArray());

            // Read through the endpoint's own client rather than through the port, so what is asserted is that the
            // server holds the object rather than that the adapter answered with a key it composed.
            Assert.Equal(payload.Length, await ObjectEndpointProbe.ReadObjectLengthAsync(services, placement.ObjectLocator!, cancellationToken));
        }
    }

    /// <summary>
    /// The payload comes back byte for byte over path-style addressing against a host that answers no bucket subdomain,
    /// signed for a region the endpoint knows nothing about. Every other test here rests on that exchange working; this
    /// is the one that states it.
    /// </summary>
    [Fact]
    public async Task FindAsync_ForAPayloadThisRunPlaced_AnswersEveryByteOfIt()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await this.StartAsync(cancellationToken);

        // Past the point at which a payload stops fitting in one buffer somebody might have sized by eye, so what the
        // exchange carries is a message rather than a token.
        var payload = PayloadOf("round-trip", 256 * 1024);
        var placement = await PlaceAsync(services, payload, cancellationToken);

        // Act
        var readBack = await services.InScopeAsync(
            (scope, token) => scope
                .GetRequiredService<IEmailContentObjectStore>()
                .FindAsync(placement.ObjectLocator!, placement.ByteLength, token),
            cancellationToken);

        // Assert
        Assert.NotNull(readBack);
        Assert.True(
            payload.Span.SequenceEqual(readBack.Value.Span),
            "The payload read back from the endpoint differs from the bytes that were written.");
    }

    /// <summary>
    /// An absent key is an answer rather than a failure. The read that meets one reports the same content-unavailable
    /// outcome a missing database payload produces, and a classifier that raised here instead would turn a repairable
    /// message into an endpoint outage.
    /// </summary>
    [Fact]
    public async Task FindAsync_ForAKeyTheEndpointDoesNotHold_AnswersWithNothing()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await this.StartAsync(cancellationToken);
        var neverWritten = await ComposeKeyAsync(services, $"incoming/{Guid.CreateVersion7()}", cancellationToken);

        // Act
        var readBack = await services.InScopeAsync(
            (scope, token) => scope
                .GetRequiredService<IEmailContentObjectStore>()
                .FindAsync(neverWritten, UnreadCeiling, token),
            cancellationToken);

        // Assert
        Assert.Null(readBack);
    }

    /// <summary>Deleting an object leaves the endpoint holding nothing under its key, which is what carries a committed deletion through to the bucket.</summary>
    [Fact]
    public async Task DeleteAsync_ForAPayloadThisRunPlaced_LeavesTheEndpointHoldingNothingUnderItsKey()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await this.StartAsync(cancellationToken);
        var placement = await PlaceAsync(services, PayloadOf("deleted"), cancellationToken);

        // Act
        await services.InScopeAsync(
            async (scope, token) =>
            {
                await scope.GetRequiredService<IEmailContentObjectStore>().DeleteAsync(placement.ObjectLocator!, token);

                return true;
            },
            cancellationToken);

        // Assert
        Assert.Null(await ObjectEndpointProbe.ReadObjectLengthAsync(services, placement.ObjectLocator!, cancellationToken));
    }

    /// <summary>
    /// Removing a key nothing holds succeeds, which is the whole of what makes the deletion path and the reclamation
    /// safe to repeat: both can reach one object, and an attempt after a crash meets a key the attempt before it
    /// already removed.
    /// </summary>
    [Fact]
    public async Task DeleteAsync_ForAKeyTheEndpointDoesNotHold_Succeeds()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await this.StartAsync(cancellationToken);
        var neverWritten = await ComposeKeyAsync(services, $"incoming/{Guid.CreateVersion7()}", cancellationToken);

        // Act
        var removed = await services.InScopeAsync(
            async (scope, token) =>
            {
                await scope.GetRequiredService<IEmailContentObjectStore>().DeleteAsync(neverWritten, token);

                return true;
            },
            cancellationToken);

        // Assert
        Assert.True(removed);
    }

    /// <summary>
    /// The listing names nothing outside this deployment's own key prefix. Two deployments sharing one bucket are
    /// separated by their prefixes alone, and reclamation deletes what the listing named — so a listing that reached
    /// outside the prefix would delete somebody else's mail rather than merely report it.
    /// </summary>
    [Fact]
    public async Task ListAsync_WithAnObjectWrittenOutsideThePrefix_NamesOnlyWhatIsBeneathIt()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await this.StartAsync(cancellationToken);
        var outsideThePrefix = $"another-deployment/{Guid.CreateVersion7()}";
        await ObjectEndpointProbe.PutObjectAsync(
            services,
            outsideThePrefix,
            PayloadOf("outside-the-prefix"),
            cancellationToken);

        // Act
        var listed = await ListWholeAsync(services, cancellationToken);

        // Assert
        Assert.DoesNotContain(outsideThePrefix, listed.Select(static held => held.Key));
        Assert.All(
            listed,
            held => Assert.StartsWith(
                $"{OrchestrationContract.ObjectStorageKeyPrefix}/",
                held.Key,
                StringComparison.Ordinal));

        // Stated so the assertion above cannot pass over an endpoint that listed nothing at all: the object outside the
        // prefix exists, and the listing is narrow rather than empty.
        Assert.NotNull(await ObjectEndpointProbe.ReadObjectLengthAsync(services, outsideThePrefix, cancellationToken));
    }

    /// <summary>
    /// A bucket holding a mailbox holds an object per message, so nothing may read the listing whole. What a sweep
    /// depends on is that a bounded page says whether it was truncated and hands back the token the next one continues
    /// from — and that continuing from it does not repeat a key, because a sweep that repeated one would page for ever.
    /// </summary>
    [Fact]
    public async Task ListAsync_OverMoreObjectsThanOnePageHolds_PagesThroughThemWithoutRepeatingOne()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await this.StartAsync(cancellationToken);

        List<string> written = [];

        for (var index = 0; index < PagedObjectCount; index++)
        {
            var placement = await PlaceAsync(services, PayloadOf($"paged-{index}"), cancellationToken);
            written.Add(placement.ObjectLocator!);
        }

        // Act
        List<string> paged = [];
        var pageCount = 0;
        string? continuationToken = null;

        do
        {
            var page = await ListAsync(services, continuationToken, PageSize, cancellationToken);

            pageCount++;
            paged.AddRange(page.Objects.Select(static held => held.Key));
            continuationToken = page.ContinuationToken;

            Assert.True(
                page.Objects.Count <= PageSize,
                $"The endpoint answered with {page.Objects.Count} objects for a page bounded at {PageSize}.");
        }
        while (continuationToken is not null);

        // Assert
        Assert.True(pageCount > 1, "The listing was not truncated, so nothing here exercised the continuation token.");
        Assert.Equal(paged.Count, paged.Distinct(StringComparer.Ordinal).Count());
        Assert.All(written, key => Assert.Contains(key, paged));
    }

    /// <summary>
    /// Every listed object states the moment the endpoint recorded it at. That moment is what the reclamation age floor
    /// is measured against, so an endpoint that reported none would leave the sweep unable to tell a write still in
    /// flight from an object nothing will ever point at.
    /// </summary>
    [Fact]
    public async Task ListAsync_ForAPayloadThisRunPlaced_StatesTheMomentTheEndpointRecordedItAt()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await this.StartAsync(cancellationToken);

        // Read before the write rather than from the clock afterwards, so the window the assertion allows is the run's
        // own and no assumption is made about the endpoint's clock agreeing with this machine's beyond a wide margin.
        var placedAfter = TimeProvider.System.GetUtcNow().AddMinutes(-5);
        var payload = PayloadOf("listed-moment");
        var placement = await PlaceAsync(services, payload, cancellationToken);

        // Act
        var listed = await ListWholeAsync(services, cancellationToken);

        // Assert
        var held = Assert.Single(listed, entry => entry.Key == placement.ObjectLocator);
        Assert.Equal(payload.Length, held.ByteLength);
        Assert.NotNull(held.WrittenAt);
        Assert.InRange(held.WrittenAt.Value, placedAfter, TimeProvider.System.GetUtcNow().AddMinutes(5));
    }

    /// <summary>
    /// A deployment that has stored nothing yet still sweeps, and what it must get is an empty listing rather than a
    /// failure. The SDK reports an absent collection as null from version 4 onwards, so this is also what proves the
    /// adapter's own handling of that rather than only the endpoint's.
    /// </summary>
    [Fact]
    public async Task ListAsync_BeneathAPrefixNothingIsWrittenUnder_AnswersAnEmptyPage()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await this.StartAsync(cancellationToken);
        var prefixNothingIsUnder = $"prefix-nothing-is-under/{Guid.CreateVersion7()}/";

        // Act
        var page = await services.InScopeAsync(
            async (scope, token) =>
            {
                using var openedClient = await scope
                    .GetRequiredService<IObjectStorageClientFactory>()
                    .OpenAsync(token);

                return await openedClient.Client.ListObjectsV2Async(
                    new ListObjectsV2Request { BucketName = Bucket, Prefix = prefixNothingIsUnder, MaxKeys = 10 },
                    token);
            },
            cancellationToken);

        // Assert
        Assert.Empty(page.S3Objects ?? []);
        Assert.NotEqual(true, page.IsTruncated);
    }

    /// <summary>
    /// The endpoint verifies the digest it is handed rather than echoing it. That is what makes the SHA-256 a row
    /// carries a statement about the object the endpoint agreed it received, instead of a statement about what the
    /// writer believed it sent — and it is the difference between a corrupted upload being refused and being stored.
    /// </summary>
    /// <remarks>
    /// Reached through the endpoint's own client because the port cannot express it: the adapter computes the digest
    /// over the bytes it is about to send, so no caller can hand it a mismatching pair.
    /// </remarks>
    [Fact]
    public async Task PutObject_WithAChecksumThePayloadDoesNotMatch_IsRefusedAndStoresNothing()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await this.StartAsync(cancellationToken);
        var payload = PayloadOf("checksum-mismatch");
        var key = await ComposeKeyAsync(services, $"incoming/{Guid.CreateVersion7()}", cancellationToken);
        var wrongDigest = Convert.ToBase64String(SHA256.HashData(PayloadOf("some other payload").Span));

        // Act
        var refusal = await services.InScopeAsync(
            async (scope, token) =>
            {
                using var openedClient = await scope
                    .GetRequiredService<IObjectStorageClientFactory>()
                    .OpenAsync(token);

                return await Assert.ThrowsAsync<AmazonS3Exception>(() => openedClient.Client.PutObjectAsync(
                    new PutObjectRequest
                    {
                        BucketName = Bucket,
                        Key = key,
                        InputStream = new MemoryStream(payload.ToArray(), writable: false),
                        ChecksumSHA256 = wrongDigest,
                    },
                    token));
            },
            cancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, refusal.StatusCode);
        Assert.Null(await ObjectEndpointProbe.ReadObjectLengthAsync(services, key, cancellationToken));
    }

    /// <summary>
    /// A key the endpoint already holds is refused rather than overwritten. Every key is minted for the write that
    /// produces it, so the endpoint answering here at all describes something this design believes impossible — and a
    /// server that accepted the second write would replace one message with another instead of reporting the collision.
    /// </summary>
    [Fact]
    public async Task PutObject_UnderAKeyTheEndpointAlreadyHolds_IsRefusedAndLeavesTheFirstPayload()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await this.StartAsync(cancellationToken);
        var first = PayloadOf("conditional-write-first");
        var second = PayloadOf("conditional-write-second", 8192);
        var placement = await PlaceAsync(services, first, cancellationToken);

        // Act
        var refusal = await services.InScopeAsync(
            async (scope, token) =>
            {
                using var openedClient = await scope
                    .GetRequiredService<IObjectStorageClientFactory>()
                    .OpenAsync(token);

                return await Assert.ThrowsAsync<AmazonS3Exception>(() => openedClient.Client.PutObjectAsync(
                    new PutObjectRequest
                    {
                        BucketName = Bucket,
                        Key = placement.ObjectLocator!,
                        InputStream = new MemoryStream(second.ToArray(), writable: false),
                        IfNoneMatch = "*",
                    },
                    token));
            },
            cancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.PreconditionFailed, refusal.StatusCode);

        var readBack = await services.InScopeAsync(
            (scope, token) => scope
                .GetRequiredService<IEmailContentObjectStore>()
                .FindAsync(placement.ObjectLocator!, placement.ByteLength, token),
            cancellationToken);
        Assert.NotNull(readBack);
        Assert.True(
            first.Span.SequenceEqual(readBack.Value.Span),
            "The refused write replaced the payload the first one had already stored.");
    }

    /// <summary>
    /// What the endpoint answers a signature it will not accept with, read by the classification the whole boundary
    /// grades failures through. An authentication failure is terminal: retrying it burns the budget on a credential
    /// that will keep being refused, and reporting it as transient is what would hide a rotated key from an operator.
    /// </summary>
    [Fact]
    public async Task Classify_ForWhatTheEndpointAnswersAWrongCredentialWith_ReportsAnAuthenticationFailure()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await this.StartAsync(cancellationToken);
        var endpoint = await services.InScopeAsync(
            (scope, _) => Task.FromResult(scope.GetRequiredService<ObjectStorageEndpoint>()),
            cancellationToken);

        using var wronglySigned = new AmazonS3Client(
            new BasicAWSCredentials(OrchestrationContract.ObjectStorageAccessKey, "not-the-secret-this-server-admits"),
            new AmazonS3Config
            {
                ServiceURL = endpoint.Address.ToString(),
                ForcePathStyle = true,
                AuthenticationRegion = OrchestrationContract.ObjectStorageRegion,
                UseHttp = endpoint.Address.Scheme == Uri.UriSchemeHttp,
            });

        // Act
        var refusal = await Assert.ThrowsAsync<AmazonS3Exception>(() => wronglySigned.ListObjectsV2Async(
            new ListObjectsV2Request { BucketName = Bucket, MaxKeys = 1 },
            cancellationToken));

        // Assert
        Assert.Equal(
            ObjectStorageFailure.AuthenticationFailed,
            ObjectStorageFailureClassifier.Classify(refusal, CancellationToken.None, CancellationToken.None));
    }

    /// <summary>Composes the services with the object backend selected, which is the deployment every test here is written against.</summary>
    private Task<OrchestratedMailFathomServices> StartAsync(CancellationToken cancellationToken) =>
        OrchestratedMailFathomServices.StartAsync(
            orchestration,
            cancellationToken,
            storesContentInObjectStorage: true);

    /// <summary>Builds a payload distinct enough that a test recognizes its own bytes, at whatever size it asked for.</summary>
    private static ReadOnlyMemory<byte> PayloadOf(string marker, int byteCount = 4096)
    {
        var text = new StringBuilder($"Subject: {marker}\r\n\r\n");

        while (text.Length < byteCount)
        {
            text.Append(marker).Append(' ');
        }

        return Encoding.ASCII.GetBytes(text.ToString(0, byteCount));
    }

    /// <summary>Places one incoming payload through the port, which is how every object this class did not hand-write got there.</summary>
    private static Task<PlacedEmailContent> PlaceAsync(
        OrchestratedMailFathomServices services,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope
                .GetRequiredService<IEmailContentObjectStore>()
                .PlaceAsync(EmailContentKind.IncomingMessage, payload, token),
            cancellationToken);

    /// <summary>Composes one whole key the way the adapter does, so a test naming a key nothing holds names one it could have held.</summary>
    private static Task<string> ComposeKeyAsync(
        OrchestratedMailFathomServices services,
        string relativeKey,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, _) => Task.FromResult(scope.GetRequiredService<ObjectStorageEndpoint>().ComposeKey(relativeKey)),
            cancellationToken);

    /// <summary>Reads one page of the objects beneath this deployment's prefix through the port under test.</summary>
    private static Task<ObjectStorageListingPage> ListAsync(
        OrchestratedMailFathomServices services,
        string? continuationToken,
        int maxObjects,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope
                .GetRequiredService<IEmailContentObjectStore>()
                .ListAsync(continuationToken, maxObjects, token),
            cancellationToken);

    /// <summary>Reads every object beneath the prefix, which the classes in this collection keep small enough to hold.</summary>
    private static async Task<IReadOnlyList<ListedObject>> ListWholeAsync(
        OrchestratedMailFathomServices services,
        CancellationToken cancellationToken)
    {
        List<ListedObject> listed = [];
        string? continuationToken = null;

        do
        {
            var page = await ListAsync(services, continuationToken, maxObjects: 1000, cancellationToken);

            listed.AddRange(page.Objects);
            continuationToken = page.ContinuationToken;
        }
        while (continuationToken is not null);

        return listed;
    }
}
