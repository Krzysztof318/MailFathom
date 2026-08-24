// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
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
    public Task<ReadOnlyMemory<byte>?> ReadBackAsync(string objectLocator, CancellationToken cancellationToken)
    {
        if (this.IsUnavailable)
        {
            throw new InvalidOperationException("The endpoint could not answer.");
        }

        if (this.CorruptedReadBack is { } corrupted)
        {
            return Task.FromResult<ReadOnlyMemory<byte>?>(corrupted);
        }

        return Task.FromResult(this.objects.TryGetValue(objectLocator, out var written)
            ? new ReadOnlyMemory<byte>(written)
            : (ReadOnlyMemory<byte>?)null);
    }
}
