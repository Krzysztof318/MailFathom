// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Release;

namespace MailFathom.Host.Api;

/// <summary>What one release freed, and how much of this deployment's database is still a copy of its bucket.</summary>
/// <param name="ReleasedPayloadCount">How many retained copies this request freed, which is none on a reading.</param>
/// <param name="ReleasedByteCount">How many bytes of raw MIME those copies were holding.</param>
/// <param name="RetainedPayloadCount">How many payloads still carry a copy beside their object.</param>
/// <param name="RetainedByteCount">How many bytes of raw MIME those copies hold between them.</param>
/// <param name="AwaitingMovePayloadCount">How many payloads the database still owns, which is what refuses a release.</param>
/// <remarks>
/// Counts and volumes and nothing else. Which payloads were freed is deliberately not served: a list of the messages a
/// deployment has just stopped keeping a second copy of would be a copy of exactly the part of the mailbox the operator
/// asked it to stop holding twice.
/// </remarks>
internal sealed record ContentReleaseResponse(
    long ReleasedPayloadCount,
    long ReleasedByteCount,
    long RetainedPayloadCount,
    long RetainedByteCount,
    long AwaitingMovePayloadCount)
{
    /// <summary>Describes one reading or one release for the wire.</summary>
    /// <param name="result">What the use case answered.</param>
    /// <returns>The response body.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="result" /> is <see langword="null" />.</exception>
    internal static ContentReleaseResponse For(RetainedContentReleaseResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new ContentReleaseResponse(
            result.Released.PayloadCount,
            result.Released.ByteCount,
            result.Retained.PayloadCount,
            result.Retained.ByteCount,
            result.AwaitingMove.PayloadCount);
    }
}
