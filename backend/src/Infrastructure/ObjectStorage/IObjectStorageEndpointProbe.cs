// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Infrastructure.ObjectStorage;

/// <summary>Establishes that the configured bucket is reachable, readable, and writable, and can be asked again whenever that has to be established.</summary>
/// <remarks>
/// <para>
/// The bucket is a remote service with a lifetime of its own: it may become reachable after this process does, its
/// credential may be rotated out from under a running deployment, and a policy change can leave it readable and no
/// longer writable. None of that is a fact about start-up, which is why the question is asked on a readiness scrape
/// rather than by a gate asking once — and why
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0017-object-storage-content-backend-consistency-and-object-identity.md">ADR 0017</see> § 1
/// makes the endpoint being taken away a readiness condition rather than a configuration error a binder could catch.
/// </para>
/// <para>
/// All three are asked because each fails on its own. An endpoint that answers a listing and refuses a write is a
/// deployment that will accept mail and be unable to store it, which is exactly the state worth finding before the first
/// message needs it rather than after.
/// </para>
/// <para>
/// <b>The probe stores no mail to find out.</b> What it writes is a zero-length object under a key of its own beneath
/// this deployment's prefix, and it removes it again; nothing about a message, an account, or a folder takes part in it.
/// </para>
/// <para>
/// Nothing registers an implementation unless the deployment selected the object-storage backend, so an instance storing
/// content in the database probes nothing.
/// </para>
/// </remarks>
public interface IObjectStorageEndpointProbe
{
    /// <summary>Verifies that the configured bucket answers a listing, accepts a write, and accepts the removal of what was written.</summary>
    /// <param name="cancellationToken">Cancels the probe.</param>
    /// <returns>A task that completes when all three succeeded.</returns>
    /// <exception cref="ObjectStorageUnavailableException">Thrown when any of the three did not, classified by what stopped it.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken" /> is cancelled.</exception>
    Task VerifyAvailableAsync(CancellationToken cancellationToken);
}
