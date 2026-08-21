// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Embeddings;
using MailFathom.Application.Emails.Embeddings.Administration;

namespace MailFathom.Application.UnitTests.TestDoubles;

/// <summary>Answers what each vector space still owes from figures a test arranged.</summary>
/// <remarks>
/// Hand-written rather than substituted so a test states the workload against a geometry rather than against a call:
/// the whole point of asking by fingerprint is that two generations are counted apart, and a substitute returning one
/// scripted answer for any argument would pass a reader that ignored the fingerprint entirely.
/// </remarks>
internal sealed class InMemoryEmbeddingWorkloadReader : IEmbeddingWorkloadReader
{
    private readonly Dictionary<EmbeddingProfileFingerprint, EmbeddingWorkload> workloads = [];

    /// <summary>Gets or sets what a geometry no test arranged answers with.</summary>
    /// <remarks>The state of a declaration nobody has activated, which is the ordinary arrangement for a first activation.</remarks>
    public EmbeddingWorkload Unarranged { get; set; } = EmbeddingWorkload.Nothing;

    /// <summary>Gets the geometries this reader was asked about, in order.</summary>
    public List<EmbeddingProfileFingerprint> Requested { get; } = [];

    /// <summary>States what one vector space still owes.</summary>
    /// <param name="identity">The geometry.</param>
    /// <param name="workload">What it owes.</param>
    public void Set(EmbeddingProfileIdentity identity, EmbeddingWorkload workload) =>
        this.workloads[EmbeddingProfileFingerprint.Compute(identity)] = workload;

    /// <inheritdoc />
    public Task<EmbeddingWorkload> ReadWorkloadAsync(
        EmbeddingProfileFingerprint geometry,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        this.Requested.Add(geometry);

        return Task.FromResult(this.workloads.GetValueOrDefault(geometry, this.Unarranged));
    }
}
