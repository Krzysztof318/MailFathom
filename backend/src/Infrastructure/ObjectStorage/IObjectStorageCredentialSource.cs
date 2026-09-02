// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Infrastructure.ObjectStorage;

/// <summary>Supplies the access key one request to the object-storage endpoint is signed with.</summary>
/// <remarks>
/// <para>
/// The port exists so the adapter holds no secret machinery at all: references, schemes, and the resolution rules stay
/// in the composition root, and what crosses is material with a defined lifetime. It is resolved per use rather than
/// once, so a key rotated behind an unchanged reference takes effect on the next call with no cache to invalidate.
/// </para>
/// <para>
/// There is no unauthenticated shape and there will not be one. An endpoint reached with no credential would be one
/// whose bucket anything on the network can read, and the payload is mail; a deployment that configured nothing must
/// fail rather than fall through to the AWS client's own resolution, which reaches environment variables, a shared
/// credentials file, and an instance metadata service. That is the whole reason this port is asked before every request
/// instead of the client being left to find a credential of its own.
/// </para>
/// </remarks>
public interface IObjectStorageCredentialSource
{
    /// <summary>Resolves the credential the next request presents.</summary>
    /// <param name="cancellationToken">Cancels the retrieval, including at the file-system boundary.</param>
    /// <returns>The credential, whose ownership passes to the caller.</returns>
    /// <exception cref="InvalidOperationException">Thrown when a configured reference could not be resolved, which is a deployment an operator has to repair rather than a failure to retry.</exception>
    Task<ObjectStorageCredential> ResolveAsync(CancellationToken cancellationToken);
}
