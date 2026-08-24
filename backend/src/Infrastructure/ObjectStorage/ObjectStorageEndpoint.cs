// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Infrastructure.ObjectStorage;

/// <summary>Where the deployment's S3-compatible endpoint is, which bucket it holds, and how a request to it is addressed.</summary>
/// <remarks>
/// <para>
/// Everything here is composed from configuration and read once, because none of it can change without the client being
/// rebuilt. What is deliberately absent is the credential: it is resolved per use through
/// <see cref="IObjectStorageCredentialSource" />, so an access key rotated behind an unchanged reference takes effect on
/// the next call with nothing to invalidate and no restart to schedule.
/// </para>
/// <para>
/// The address is explicit and so is the signing region, which is the property the whole type exists for. The AWS client
/// resolves both from the environment, a shared credentials file, and an instance metadata service when it is not told
/// them, and a deployment that forgot to configure one would then quietly acquire the host's own identity and reach a
/// metadata endpoint. <see cref="Region" /> therefore always carries a value — the configured one, or
/// <see cref="DefaultRegion" /> for an endpoint that has no notion of a region at all — so nothing is ever left for the
/// client to discover.
/// </para>
/// <para>
/// The key prefix is normalized to end in a single <c>/</c> when it is present, so every consumer composes a key the
/// same way and a shared bucket's prefixes stay disjoint. An empty prefix is an endpoint whose bucket MailFathom has to
/// itself, which is the shape a self-hosted deployment usually takes.
/// </para>
/// </remarks>
public sealed record ObjectStorageEndpoint
{
    /// <summary>The name of the outbound client every request to the endpoint is made through.</summary>
    /// <remarks>
    /// Declared on this type rather than on a consumer, because the adapter and the health check reach one endpoint
    /// under one set of bounds and a name on one of them would leave the other reading a string it does not own.
    /// <c>CreateClient</c> with an unregistered name answers with an unbounded client rather than failing, so the
    /// agreement between the registration and both call sites has to be a compile-time one.
    /// </remarks>
    public const string TransportName = "object-storage";

    /// <summary>The signing region an endpoint that publishes none is addressed under.</summary>
    /// <remarks>
    /// SigV4 puts a region into the credential scope whether or not the endpoint has one, so a value is always sent. The
    /// S3-compatible implementations that ignore it — MinIO, Ceph, Garage, and Silo among them — accept this one, and it
    /// is the value their own documentation uses, so a deployment that names nothing signs the way every sample does.
    /// </remarks>
    public const string DefaultRegion = "us-east-1";

    private ObjectStorageEndpoint(
        Uri address,
        string bucket,
        string keyPrefix,
        string region,
        bool usePathStyleAddressing,
        TimeSpan connectTimeout,
        TimeSpan requestTimeout)
    {
        this.Address = address;
        this.Bucket = bucket;
        this.KeyPrefix = keyPrefix;
        this.Region = region;
        this.UsePathStyleAddressing = usePathStyleAddressing;
        this.ConnectTimeout = connectTimeout;
        this.RequestTimeout = requestTimeout;
    }

    /// <summary>Gets the absolute address requests are sent to.</summary>
    public Uri Address { get; }

    /// <summary>Gets the bucket every object is written into and read from.</summary>
    public string Bucket { get; }

    /// <summary>Gets the prefix every key this deployment writes begins with, ending in <c>/</c>, or the empty string for none.</summary>
    public string KeyPrefix { get; }

    /// <summary>Gets the region a request is signed under.</summary>
    public string Region { get; }

    /// <summary>Gets whether the bucket is addressed in the path rather than in the host name.</summary>
    /// <remarks>
    /// On by default and off only for an endpoint that genuinely serves virtual-hosted buckets. Path-style is what a
    /// self-hosted endpoint reached by address or by a service name inside a cluster can answer at all, since
    /// virtual-hosted addressing needs a wildcard DNS name and a certificate to match it.
    /// </remarks>
    public bool UsePathStyleAddressing { get; }

    /// <summary>Gets how long establishing a connection to the endpoint may take.</summary>
    /// <remarks>
    /// Separate from <see cref="RequestTimeout" /> because the two answer different questions. An endpoint whose address
    /// resolves to nothing, or whose port is filtered, otherwise consumes a whole request budget doing nothing; bounding
    /// the connect alone turns that into a fast transport failure the attempt after it can act on.
    /// </remarks>
    public TimeSpan ConnectTimeout { get; }

    /// <summary>Gets how long one whole request to the endpoint may take, connection included.</summary>
    /// <remarks>
    /// It is the transport's backstop rather than the operation's budget. What bounds an operation is
    /// <c>Resilience:ObjectStorageInvocation</c>, which also decides how many attempts it gets, so this is set above one
    /// attempt's timeout: a request cut here rather than there would report a transport failure where the operator
    /// configured a budget.
    /// </remarks>
    public TimeSpan RequestTimeout { get; }

    /// <summary>Composes the endpoint a deployment's configuration describes.</summary>
    /// <param name="address">The absolute address requests are sent to.</param>
    /// <param name="bucket">The bucket every object is written into and read from.</param>
    /// <param name="keyPrefix">The prefix keys are written under, which may be empty and is normalized to end in <c>/</c>.</param>
    /// <param name="region">The signing region, which may be empty and then takes <see cref="DefaultRegion" />.</param>
    /// <param name="usePathStyleAddressing">Whether the bucket is addressed in the path rather than in the host name.</param>
    /// <param name="connectTimeout">How long establishing a connection may take.</param>
    /// <param name="requestTimeout">How long one whole request may take.</param>
    /// <returns>The composed endpoint.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="address" />, <paramref name="bucket" />, <paramref name="keyPrefix" />, or <paramref name="region" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="address" /> is not absolute, or <paramref name="bucket" /> is blank.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when either timeout is not positive.</exception>
    /// <remarks>Called only after configuration validation has passed, so what is left here is normalization rather than a second set of rules.</remarks>
    public static ObjectStorageEndpoint Create(
        Uri address,
        string bucket,
        string keyPrefix,
        string region,
        bool usePathStyleAddressing,
        TimeSpan connectTimeout,
        TimeSpan requestTimeout)
    {
        ArgumentNullException.ThrowIfNull(address);
        ArgumentException.ThrowIfNullOrWhiteSpace(bucket);
        ArgumentNullException.ThrowIfNull(keyPrefix);
        ArgumentNullException.ThrowIfNull(region);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(connectTimeout, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(requestTimeout, TimeSpan.Zero);

        if (!address.IsAbsoluteUri)
        {
            throw new ArgumentException("An object-storage endpoint is addressed absolutely.", nameof(address));
        }

        return new ObjectStorageEndpoint(
            address,
            bucket.Trim(),
            NormalizeKeyPrefix(keyPrefix),
            region.Trim() is { Length: > 0 } configuredRegion ? configuredRegion : DefaultRegion,
            usePathStyleAddressing,
            connectTimeout,
            requestTimeout);
    }

    /// <summary>Composes the key an object is stored under, beneath this deployment's own prefix.</summary>
    /// <param name="relativeKey">The key within the prefix, which never begins with <c>/</c>.</param>
    /// <returns>The whole key.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="relativeKey" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="relativeKey" /> is empty or whitespace.</exception>
    public string ComposeKey(string relativeKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativeKey);

        return this.KeyPrefix + relativeKey.TrimStart('/');
    }

    private static string NormalizeKeyPrefix(string keyPrefix)
    {
        var trimmedPrefix = keyPrefix.Trim().Trim('/');

        return trimmedPrefix.Length == 0 ? string.Empty : trimmedPrefix + "/";
    }
}
