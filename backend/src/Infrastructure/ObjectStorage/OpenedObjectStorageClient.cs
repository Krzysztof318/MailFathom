// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using Amazon.S3;

namespace MailFathom.Infrastructure.ObjectStorage;

/// <summary>One operation's client for the object-storage endpoint, together with the credential it was signed with.</summary>
/// <remarks>
/// Both are released when the operation ends, which bounds the window in which a process dump could hold the access key
/// to one call rather than to process uptime. The client is disposed first because it is what holds the credential, and
/// the transport underneath it belongs to the outbound client factory rather than to this instance.
/// </remarks>
internal sealed class OpenedObjectStorageClient : IDisposable
{
    private readonly IDisposable? ownedClient;
    private readonly ObjectStorageCredential? credential;

    /// <summary>Initializes a lease over a client and the credential it presents.</summary>
    /// <param name="client">The client every request of the operation is sent through.</param>
    /// <param name="ownedClient">The client's own disposal, or <see langword="null" /> when the caller supplied one it owns itself.</param>
    /// <param name="credential">The credential released with the client, or <see langword="null" /> when the caller holds none.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="client" /> is <see langword="null" />.</exception>
    internal OpenedObjectStorageClient(
        IAmazonS3 client,
        IDisposable? ownedClient,
        ObjectStorageCredential? credential)
    {
        ArgumentNullException.ThrowIfNull(client);

        this.Client = client;
        this.ownedClient = ownedClient;
        this.credential = credential;
    }

    /// <summary>Gets the client every request of the operation is sent through.</summary>
    internal IAmazonS3 Client { get; }

    /// <inheritdoc />
    public void Dispose()
    {
        this.ownedClient?.Dispose();
        this.credential?.Dispose();
    }
}
