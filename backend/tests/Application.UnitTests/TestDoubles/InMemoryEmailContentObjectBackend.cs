// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Security.Cryptography;
using MailFathom.Application.EmailContent.Storage;

namespace MailFathom.Application.UnitTests.TestDoubles;

/// <summary>An object backend that keeps what it was given, and can be made to lose it or hand back something else.</summary>
/// <remarks>
/// The three arranged behaviours are the three the move has to survive: an endpoint that answered and holds nothing, one
/// that hands back bytes that are not the message, and one that cannot answer at all.
/// </remarks>
internal sealed class InMemoryEmailContentObjectBackend : IEmailContentObjectBackend
{
    private readonly Dictionary<string, byte[]> objects = new(StringComparer.Ordinal);

    /// <summary>Gets how many objects were written, which is what proves a refusal wrote none.</summary>
    internal int PlacementCount { get; private set; }

    /// <summary>Gets the keys the backend holds, in the order they were written.</summary>
    internal IReadOnlyCollection<string> Keys => this.objects.Keys;

    /// <summary>Gets or sets whether a written object is there to be read back.</summary>
    internal bool KeepsWhatItIsGiven { get; set; } = true;

    /// <summary>Gets or sets bytes the read-back hands over instead of the object, standing in for a damaged one.</summary>
    internal byte[]? CorruptedReadBack { get; set; }

    /// <summary>Gets or sets whether the endpoint refuses to answer at all.</summary>
    internal bool IsUnavailable { get; set; }

    /// <summary>Gets the ceiling the last read-back was bounded by, which is what proves the caller stated the length its row records.</summary>
    internal long? ReadBackCeiling { get; private set; }

    /// <summary>Gets or sets what happens the moment an object is written, given which placement this is, counted from one.</summary>
    /// <remarks>The seam a test interrupts a payload mid-carry through, which is the one point where the pass is holding a message and has not yet repointed its row.</remarks>
    internal Action<int>? WhenPlacing { get; set; }

    /// <inheritdoc />
    public Task<PlacedEmailContent> PlaceAsync(
        EmailContentKind kind,
        ReadOnlyMemory<byte> rawMime,
        CancellationToken cancellationToken)
    {
        if (this.IsUnavailable)
        {
            throw new InvalidOperationException("The endpoint could not answer.");
        }

        this.PlacementCount++;
        this.WhenPlacing?.Invoke(this.PlacementCount);

        var objectLocator = $"mail/{kind}/{this.PlacementCount:D4}";

        if (this.KeepsWhatItIsGiven)
        {
            this.objects[objectLocator] = rawMime.ToArray();
        }

        return Task.FromResult(PlacedEmailContent.InObjectStorage(
            objectLocator,
            rawMime.Length,
            SHA256.HashData(rawMime.Span)));
    }

    /// <inheritdoc />
    /// <remarks>Reads one byte past the ceiling exactly as the endpoint does, so a test arranging an over-long answer meets the bound rather than the whole of it.</remarks>
    public Task<ReadOnlyMemory<byte>?> ReadBackAsync(
        string objectLocator,
        long maximumByteLength,
        CancellationToken cancellationToken)
    {
        if (this.IsUnavailable)
        {
            throw new InvalidOperationException("The endpoint could not answer.");
        }

        this.ReadBackCeiling = maximumByteLength;

        if (this.CorruptedReadBack is { } corrupted)
        {
            return Task.FromResult<ReadOnlyMemory<byte>?>(Bounded(corrupted, maximumByteLength));
        }

        return Task.FromResult(this.objects.TryGetValue(objectLocator, out var written)
            ? Bounded(written, maximumByteLength)
            : (ReadOnlyMemory<byte>?)null);
    }

    /// <summary>Hands back at most one byte past the ceiling, which is what the endpoint stops reading at.</summary>
    private static ReadOnlyMemory<byte> Bounded(byte[] answer, long maximumByteLength) =>
        new ReadOnlyMemory<byte>(answer)[..(int)Math.Min(answer.LongLength, maximumByteLength + 1)];
}
