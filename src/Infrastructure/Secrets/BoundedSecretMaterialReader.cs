// Copyright © 2026 Krzysztof Kasprowicz

using System.Security.Cryptography;

namespace MailMcp.Infrastructure.Secrets;

/// <summary>Reads secret material from a stream under an explicit size ceiling, erasing every intermediate buffer.</summary>
/// <remarks>
/// The ceiling is enforced while reading rather than after it, so an oversized target is a named failure instead of an
/// allocation. Every buffer the read allocates is pinned and zeroed, including the ones abandoned while growing:
/// erasing only the copy that becomes the owned material would leave the credential in a movable array until the
/// collector happened to reclaim it.
/// </remarks>
internal static class BoundedSecretMaterialReader
{
    private const int InitialBufferByteCount = 4096;

    /// <summary>Reads the whole stream as one secret.</summary>
    /// <param name="source">The stream positioned at the material.</param>
    /// <param name="maximumByteCount">The ceiling, above which the read fails instead of allocating.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The owned material, or <see cref="SecretResolutionFailure.MaterialEmpty" /> or <see cref="SecretResolutionFailure.MaterialTooLarge" />.</returns>
    internal static async Task<SecretResolutionResult> ReadAsync(
        Stream source,
        int maximumByteCount,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumByteCount);

        // One byte of headroom above the ceiling makes an oversized target observable without ever holding it.
        var capacityCeiling = maximumByteCount + 1;
        var buffer = GC.AllocateArray<byte>(Math.Min(InitialBufferByteCount, capacityCeiling), pinned: true);
        var byteCount = 0;

        try
        {
            while (true)
            {
                // A read that crossed the ceiling has already returned, so the buffer is never full at its ceiling
                // capacity here and growth always makes progress.
                if (byteCount == buffer.Length)
                {
                    buffer = GrowAndEraseSource(buffer, byteCount, capacityCeiling);
                }

                var read = await source.ReadAsync(buffer.AsMemory(byteCount), cancellationToken);
                if (read == 0)
                {
                    break;
                }

                byteCount += read;
                if (byteCount > maximumByteCount)
                {
                    return SecretResolutionResult.Failed(SecretResolutionFailure.MaterialTooLarge);
                }
            }

            return byteCount == 0
                ? SecretResolutionResult.Failed(SecretResolutionFailure.MaterialEmpty)
                : SecretResolutionResult.Resolved(
                    ResolvedSecret.FromBytes(buffer.AsSpan(0, byteCount)),
                    SecretMaterialSource.SchemeAdapter);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(buffer);
        }
    }

    private static byte[] GrowAndEraseSource(byte[] current, int byteCount, int capacityCeiling)
    {
        var grownLength = (int)Math.Min((long)current.Length * 2L, capacityCeiling);
        var grown = GC.AllocateArray<byte>(grownLength, pinned: true);
        current.AsSpan(0, byteCount).CopyTo(grown);
        CryptographicOperations.ZeroMemory(current);

        return grown;
    }
}
