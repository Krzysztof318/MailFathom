// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Secrets.Resolution;

namespace MailFathom.Infrastructure.Secrets.Sources;

/// <summary>Reads deployment-provisioned secret material from a file.</summary>
/// <remarks>
/// The port exists so scheme adapters are unit-testable without touching the real file system. It hands ownership of
/// the material to the caller and retains no copy: an implementation that returned the raw bytes instead would leave an
/// un-erasable intermediate array behind for exactly the two schemes production uses. It is asynchronous and takes the
/// caller's token so that a read of a stalled network-mounted secret path is a request the caller can withdraw from.
/// The token is not what protects against such a path, because the open beneath it reaches no token; the implementation
/// states what does.
/// </remarks>
internal interface ISecretFileReader
{
    /// <summary>Reads at most <paramref name="maximumByteCount" /> bytes of material.</summary>
    /// <param name="path">The absolute path of the provisioned file.</param>
    /// <param name="maximumByteCount">The ceiling enforced while reading, before an owned buffer is allocated.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The owned material, or a named failure such as <see cref="SecretResolutionFailure.MaterialTooLarge" />.</returns>
    Task<SecretResolutionResult> ReadAsync(string path, int maximumByteCount, CancellationToken cancellationToken);
}
