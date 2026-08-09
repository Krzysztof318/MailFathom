// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.AiProviders;
using MailFathom.Application.Emails.Embeddings.Backfill;
using MailFathom.Application.Emails.Embeddings.Generations;
using MailFathom.Application.Emails.Embeddings.Limits;

namespace MailFathom.Application.Emails.Embeddings.Administration;

/// <summary>Answers, in one read, whether semantic search is working on this instance and how far behind it is.</summary>
/// <remarks>
/// Composed here rather than by whatever surface asks, because the composition is the answer: which generation serves,
/// how much each one still owes, what the provider last did, what the budget period has spent, and when the walk next
/// runs are five sources that only mean something together. Assembling them in the endpoint would put that reasoning in
/// the composition root and would leave a second caller to reassemble it differently.
/// </remarks>
public sealed class EmbeddingStatusReader
{
    private readonly IEmbeddingGenerationStore generationStore;
    private readonly IEmbeddingWorkloadReader workloadReader;
    private readonly EmbeddingSpendGate spendGate;
    private readonly IAiProviderHealthReader providerHealth;
    private readonly EmbeddingBackfillSchedule backfillSchedule;

    /// <summary>Initializes a new reader over the state one status answer is composed from.</summary>
    /// <param name="generationStore">Reads which generations this instance holds.</param>
    /// <param name="workloadReader">Counts what each generation still owes.</param>
    /// <param name="spendGate">Reads where the budget period stands.</param>
    /// <param name="providerHealth">Reports what the last call to the embedding provider established.</param>
    /// <param name="backfillSchedule">Reports when the walk's next pass is due.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public EmbeddingStatusReader(
        IEmbeddingGenerationStore generationStore,
        IEmbeddingWorkloadReader workloadReader,
        EmbeddingSpendGate spendGate,
        IAiProviderHealthReader providerHealth,
        EmbeddingBackfillSchedule backfillSchedule)
    {
        ArgumentNullException.ThrowIfNull(generationStore);
        ArgumentNullException.ThrowIfNull(workloadReader);
        ArgumentNullException.ThrowIfNull(spendGate);
        ArgumentNullException.ThrowIfNull(providerHealth);
        ArgumentNullException.ThrowIfNull(backfillSchedule);

        this.generationStore = generationStore;
        this.workloadReader = workloadReader;
        this.spendGate = spendGate;
        this.providerHealth = providerHealth;
        this.backfillSchedule = backfillSchedule;
    }

    /// <summary>Reads where semantic search stands on this instance.</summary>
    /// <param name="declared">The geometry configuration declares, or <see langword="null" /> where it declares none.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>The status.</returns>
    /// <exception cref="OperationCanceledException">Thrown when the caller cancels.</exception>
    /// <remarks>
    /// An instance that has activated nothing answers with both generations absent and the rest present, which is the
    /// supported deployment that serves lexical search rather than a failure to report on.
    /// </remarks>
    public async Task<EmbeddingStatus> ReadAsync(
        EmbeddingProfileIdentity? declared,
        CancellationToken cancellationToken)
    {
        var generations = await this.generationStore.ReadGenerationsAsync(cancellationToken);

        return new EmbeddingStatus(
            declared,
            await this.DescribeAsync(generations.Serving, cancellationToken),
            await this.DescribeAsync(generations.Building, cancellationToken),
            this.providerHealth.Read(AiProviderRole.Embedding),
            await this.spendGate.ReadCurrentPeriodAsync(cancellationToken),
            this.backfillSchedule.NextPassDueAt);
    }

    /// <summary>Counts what one generation still owes, where there is a generation to count for.</summary>
    private async Task<EmbeddingGenerationProgress?> DescribeAsync(
        RegisteredEmbeddingProfile? generation,
        CancellationToken cancellationToken)
    {
        if (generation is not { } present)
        {
            return null;
        }

        var workload = await this.workloadReader.ReadWorkloadAsync(
            EmbeddingProfileFingerprint.Compute(present.Identity),
            cancellationToken);

        return new EmbeddingGenerationProgress(present, workload);
    }
}
