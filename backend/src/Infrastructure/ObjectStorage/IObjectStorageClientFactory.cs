// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Infrastructure.ObjectStorage;

/// <summary>Opens a client for one operation against the configured object-storage endpoint.</summary>
/// <remarks>
/// <para>
/// A client per operation rather than one held for the process, because the credential is resolved per use and a client
/// is what carries it. What that costs is a small object; what it buys is a key rotated behind an unchanged reference
/// taking effect on the next operation, with no cache to invalidate and no restart to schedule. The transport underneath
/// is the pooled one the outbound client factory owns, so nothing reconnects for it.
/// </para>
/// <para>
/// The port exists for the seam a test needs. Every caller here talks to the endpoint through the client the returned
/// lease exposes, which is an interface the SDK publishes and a substitute can script, so a test states what the
/// endpoint answered rather than opening a socket.
/// </para>
/// </remarks>
internal interface IObjectStorageClientFactory
{
    /// <summary>Gets what the client is opened against, so a caller composes keys without holding a second copy of the settings.</summary>
    ObjectStorageEndpoint Endpoint { get; }

    /// <summary>Resolves the credential and opens a client for one operation.</summary>
    /// <param name="cancellationToken">Cancels the credential resolution.</param>
    /// <returns>The opened client, whose ownership passes to the caller.</returns>
    /// <exception cref="InvalidOperationException">Thrown when a configured credential reference could not be resolved.</exception>
    Task<OpenedObjectStorageClient> OpenAsync(CancellationToken cancellationToken);
}
