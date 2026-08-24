// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Amazon.S3;
using Amazon.S3.Model;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Infrastructure.Observability;

namespace MailFathom.Infrastructure.ObjectStorage;

/// <summary>Stores raw MIME payloads as objects in the configured S3-compatible bucket.</summary>
/// <remarks>
/// <para>
/// A key is minted per write and never derived from anything, which is what lets a payload be written before the row
/// that points at it exists — and that ordering is the whole design: the object is durable before the unit of work
/// commits, so no committed row can point at mail that is not there. The cost is an object nothing points at whenever
/// that unit of work does not commit, which reclamation removes.
/// </para>
/// <para>
/// Nothing here logs a key or a payload. A key names one message, so it is personal data in the same way a folder name
/// or a subject is, and the telemetry this goes through records volumes and classifications rather than identities.
/// </para>
/// </remarks>
internal sealed class S3EmailContentObjectStore : IEmailContentObjectStore
{
    private readonly IObjectStorageClientFactory clientFactory;
    private readonly ObjectStorageOperationRunner operationRunner;

    /// <summary>Initializes the store.</summary>
    /// <param name="clientFactory">Opens the client each request is made through.</param>
    /// <param name="operationRunner">Runs each request under the object-storage budget and classifies what stopped it.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public S3EmailContentObjectStore(
        IObjectStorageClientFactory clientFactory,
        ObjectStorageOperationRunner operationRunner)
    {
        ArgumentNullException.ThrowIfNull(clientFactory);
        ArgumentNullException.ThrowIfNull(operationRunner);

        this.clientFactory = clientFactory;
        this.operationRunner = operationRunner;
    }

    /// <inheritdoc />
    public async Task<PlacedEmailContent> PlaceAsync(
        EmailContentKind kind,
        ReadOnlyMemory<byte> rawMime,
        CancellationToken cancellationToken)
    {
        if (rawMime.IsEmpty)
        {
            throw new ArgumentException("Raw MIME content to place cannot be empty.", nameof(rawMime));
        }

        var endpoint = this.clientFactory.Endpoint;
        var objectKey = endpoint.ComposeKey($"{SegmentOf(kind)}/{Guid.CreateVersion7()}");
        var digest = SHA256.HashData(rawMime.Span);

        // Resolved once, outside the attempt, because a message is the largest thing this process holds and a copy per
        // attempt would price a retry at the message. The usual caller already hands over a complete array, in which
        // case this copies nothing at all.
        var payload = CompleteArrayOf(rawMime);

        using var openedClient = await this.clientFactory.OpenAsync(cancellationToken);

        await this.operationRunner.RunAsync(
            ObjectStorageTelemetry.PutOperationName,
            attemptToken => openedClient.Client.PutObjectAsync(
                new PutObjectRequest
                {
                    BucketName = endpoint.Bucket,
                    Key = objectKey,

                    // A stream of its own per attempt over that one buffer, rather than one stream shared across them.
                    // A stream read to its end would upload nothing on a repeat and report success for it, which is the
                    // one failure a retry must not be able to produce here.
                    InputStream = new MemoryStream(payload, writable: false),

                    // The endpoint verifies the upload against this and rejects a corrupted one rather than storing it,
                    // so a row carrying this digest describes an object the endpoint agreed it received intact.
                    ChecksumSHA256 = Convert.ToBase64String(digest),

                    // The key was minted for this write, so an endpoint that finds one already there is reporting
                    // something this design believes impossible. It is a failure rather than a repeat to absorb.
                    IfNoneMatch = "*",
                },
                attemptToken),
            _ => rawMime.Length,
            cancellationToken);

        return PlacedEmailContent.InObjectStorage(objectKey, rawMime.Length, digest);
    }

    /// <inheritdoc />
    public async Task<ReadOnlyMemory<byte>?> FindAsync(string objectLocator, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectLocator);

        var endpoint = this.clientFactory.Endpoint;

        using var openedClient = await this.clientFactory.OpenAsync(cancellationToken);

        var payload = await this.operationRunner.RunAsync(
            ObjectStorageTelemetry.GetOperationName,
            attemptToken => ReadObjectAsync(openedClient.Client, endpoint.Bucket, objectLocator, attemptToken),
            answer => answer?.Length,
            cancellationToken);

        // Written as a branch rather than as a conditional expression. The null literal converts to `byte[]` and on to
        // `ReadOnlyMemory<byte>` through that type's own implicit operator, so a conditional would give the absent case
        // the natural type of the other branch and answer an empty payload where this answers nothing.
        if (payload is null)
        {
            return null;
        }

        return new ReadOnlyMemory<byte>(payload);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string objectLocator, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectLocator);

        var endpoint = this.clientFactory.Endpoint;

        using var openedClient = await this.clientFactory.OpenAsync(cancellationToken);

        await this.operationRunner.RunAsync(
            ObjectStorageTelemetry.DeleteOperationName,
            attemptToken => openedClient.Client.DeleteObjectAsync(
                new DeleteObjectRequest { BucketName = endpoint.Bucket, Key = objectLocator },
                attemptToken),
            _ => null,
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ObjectStorageListingPage> ListAsync(
        string? continuationToken,
        int maxObjects,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maxObjects, 0);

        var endpoint = this.clientFactory.Endpoint;

        using var openedClient = await this.clientFactory.OpenAsync(cancellationToken);

        var answer = await this.operationRunner.RunAsync(
            ObjectStorageTelemetry.ListOperationName,
            attemptToken => openedClient.Client.ListObjectsV2Async(
                new ListObjectsV2Request
                {
                    BucketName = endpoint.Bucket,

                    // An empty prefix is the whole bucket, which is what a deployment that has one to itself configured.
                    // Where a prefix is configured this is the only thing keeping the listing inside it, and reclamation
                    // deletes what the listing named.
                    Prefix = endpoint.KeyPrefix,
                    ContinuationToken = continuationToken,
                    MaxKeys = maxObjects,
                },
                attemptToken),
            _ => null,
            cancellationToken);

        // The SDK leaves an absent collection null rather than empty from version 4 onwards, and a bucket holding
        // nothing beneath the prefix is exactly that case.
        var listed = answer.S3Objects ?? [];

        return new ObjectStorageListingPage(
            [.. listed.Select(static held => new ListedObject(held.Key, WrittenAtOf(held), held.Size ?? 0))],

            // Read from the answer's own flag rather than from the token being present, because an endpoint that ended
            // the listing may still echo a token, and continuing from one would list the same page for ever.
            answer.IsTruncated == true ? answer.NextContinuationToken : null);
    }

    /// <summary>Reads the moment an endpoint recorded one object at, as the instant an age is measured from.</summary>
    /// <remarks>
    /// The SDK reports it as a kind-free <see cref="DateTime" /> that S3 defines as UTC, and answers with none where
    /// the endpoint named none. That absence is carried through rather than resolved to an instant: a moment invented
    /// here would decide the age floor, and the floor is the only thing standing between reclamation and a payload
    /// whose unit of work has not committed yet.
    /// </remarks>
    private static DateTimeOffset? WrittenAtOf(S3Object held) => held.LastModified is { } writtenAt
        ? new DateTimeOffset(DateTime.SpecifyKind(writtenAt, DateTimeKind.Utc))
        : null;

    /// <summary>Reads one object whole, answering with nothing when the endpoint holds none under that key.</summary>
    /// <remarks>
    /// The absent case is resolved here rather than by the failure classifier, because it is not a failure: it reaches
    /// the caller as the same content-unavailable answer a missing database payload produces. Resolving it inside the
    /// attempt is also what keeps it from being retried — a key nothing holds will not be held by the attempt after it.
    /// </remarks>
    private static async Task<byte[]?> ReadObjectAsync(
        IAmazonS3 client,
        string bucket,
        string objectKey,
        CancellationToken cancellationToken)
    {
        try
        {
            using var answer = await client.GetObjectAsync(
                new GetObjectRequest { BucketName = bucket, Key = objectKey },
                cancellationToken);

            using var payload = new MemoryStream();
            await answer.ResponseStream.CopyToAsync(payload, cancellationToken);

            return payload.ToArray();
        }
        catch (AmazonS3Exception absent) when (absent.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    /// <summary>Gets the payload as one array the whole of which is the payload, copying only when it is not already one.</summary>
    /// <remarks>
    /// The SDK's request carries a stream rather than a buffer, and a stream over a segment of a larger array would
    /// upload the wrong bytes. Every caller in this system hands over a complete array, so the copy is the path nothing
    /// takes rather than the path everything pays for.
    /// </remarks>
    private static byte[] CompleteArrayOf(ReadOnlyMemory<byte> rawMime) =>
        MemoryMarshal.TryGetArray(rawMime, out var segment)
        && segment.Offset == 0
        && segment.Count == segment.Array!.Length
            ? segment.Array
            : rawMime.ToArray();

    /// <summary>Names the group of keys one payload kind is written under.</summary>
    /// <remarks>
    /// A readability and grouping property rather than a durable identity: every row carries the whole key it was
    /// written under, and nothing here ever derives one from a row, so what these words are only decides what an
    /// operator sees in a listing.
    /// </remarks>
    private static string SegmentOf(EmailContentKind kind) => kind switch
    {
        EmailContentKind.IncomingMessage => "incoming",
        EmailContentKind.OutgoingMessage => "outgoing",
        EmailContentKind.RecurringSendDraft => "recurring-send-drafts",
        EmailContentKind.MailDraft => "mail-drafts",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "The payload kind names no group of keys."),
    };
}
