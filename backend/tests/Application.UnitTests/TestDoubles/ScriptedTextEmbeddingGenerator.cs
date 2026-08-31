// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Embeddings;
using MailFathom.Application.SensitiveContent.Egress;

namespace MailFathom.Application.UnitTests.TestDoubles;

/// <summary>A generator that answers from the text alone, and can be told to fail on a chosen call.</summary>
/// <remarks>
/// Hand-written rather than substituted because the tests assert how many calls were made with which passages and what
/// a failure part-way through leaves behind, and a recorded list of batches reports all three without a matcher.
/// </remarks>
internal sealed class ScriptedTextEmbeddingGenerator : ITextEmbeddingGenerator
{
    private readonly List<IReadOnlyList<string>> requestedBatches = [];

    /// <summary>Initializes a generator producing vectors of one space.</summary>
    public ScriptedTextEmbeddingGenerator(EmbeddingProfileIdentity identity, int maximumPassagesPerCall)
    {
        this.Identity = identity;
        this.MaximumPassagesPerCall = maximumPassagesPerCall;
    }

    /// <inheritdoc />
    public EmbeddingProfileIdentity Identity { get; }

    /// <inheritdoc />
    public int MaximumPassagesPerCall { get; }

    /// <summary>Gets or sets the failure raised instead of vectors, or <see langword="null" /> to always answer.</summary>
    public EmbeddingGenerationFailure? Failure { get; set; }

    /// <summary>Gets or sets which call fails, counted from one, once <see cref="Failure" /> is set.</summary>
    public int FailingCallNumber { get; set; } = 1;

    /// <summary>Gets or sets a token observed on every call, so a test can cancel from inside the provider.</summary>
    public CancellationTokenSource? CancelOnCall { get; set; }

    /// <summary>Gets or sets the guard the real adapter scans a batch through, or <see langword="null" /> to send it unscanned.</summary>
    /// <remarks>
    /// The provider adapter guards its passages at
    /// <see cref="SensitiveContentEgressPoint.HostedEmbeddingInput" /> before any endpoint is reached, so whose posture
    /// a batch left under is decided here rather than by the caller. A test about that has to set this; one about
    /// batching, budgets, or failures leaves it null and the double behaves as it always did.
    /// </remarks>
    public SensitiveContentEgressGuard? EgressGuard { get; set; }

    /// <summary>Gets the batches this generator was asked for, in order.</summary>
    public IReadOnlyList<IReadOnlyList<string>> RequestedBatches => this.requestedBatches;

    /// <inheritdoc />
    public async Task<IReadOnlyList<EmbeddingVector>> GenerateAsync(
        IReadOnlyList<string> passages,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(passages);
        cancellationToken.ThrowIfCancellationRequested();

        var sent = this.EgressGuard is { } egressGuard
            ? await egressGuard.GuardAllAsync(
                SensitiveContentEgressPoint.HostedEmbeddingInput,
                passages,
                cancellationToken)
            : passages;

        this.requestedBatches.Add(sent);
        this.CancelOnCall?.Cancel();

        if (this.Failure is { } failure && this.requestedBatches.Count == this.FailingCallNumber)
        {
            throw new EmbeddingGenerationFailedException("scripted", failure);
        }

        IReadOnlyList<EmbeddingVector> vectors = [.. sent.Select(this.Place)];

        return vectors;
    }

    /// <summary>Places a passage at a point derived from its own text, so the same passage always lands identically.</summary>
    private EmbeddingVector Place(string passage)
    {
        var components = new float[this.Identity.Dimension];
        var axis = (uint)string.GetHashCode(passage, StringComparison.Ordinal) % (uint)components.Length;
        components[axis] = 1;

        return EmbeddingVector.Create(components);
    }
}
