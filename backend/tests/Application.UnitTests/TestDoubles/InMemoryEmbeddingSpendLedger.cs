// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Embeddings.Limits;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Access;

namespace MailFathom.Application.UnitTests.TestDoubles;

/// <summary>Keeps what each period and owner has spent in memory, adding exactly as the real upsert does.</summary>
/// <remarks>
/// Hand-written rather than substituted, because what the gate is asked and what it later writes have to agree: a
/// substitute answering the read from a script would report a period as admitting a request after the same test had
/// already spent it, and every ceiling assertion would then be about the script instead of about the ledger. It keys by
/// period and owner together for the same reason the table does — a fake that summed both owners into one figure would
/// let a per-owner ceiling pass a test it does not enforce.
/// </remarks>
internal sealed class InMemoryEmbeddingSpendLedger : IEmbeddingSpendLedger
{
    private readonly Dictionary<(DateTimeOffset PeriodStart, MailOwnerId Owner), long> consumedByPeriodAndOwner = [];

    /// <summary>Gets what each period and owner has been charged so far.</summary>
    public IReadOnlyDictionary<(DateTimeOffset PeriodStart, MailOwnerId Owner), long> ConsumedByPeriodAndOwner =>
        this.consumedByPeriodAndOwner;

    /// <summary>Gets what each period has been charged across every owner.</summary>
    public IReadOnlyDictionary<DateTimeOffset, long> ConsumedByPeriod => this.consumedByPeriodAndOwner
        .GroupBy(charge => charge.Key.PeriodStart)
        .ToDictionary(period => period.Key, period => period.Sum(charge => charge.Value));

    /// <summary>Charges a period before the test begins, which is how a test starts against a partly spent ceiling.</summary>
    /// <param name="periodStart">The period to charge.</param>
    /// <param name="owner">The owner the spend is attributed to.</param>
    /// <param name="inputCharacterCount">The characters to charge it.</param>
    public void Seed(DateTimeOffset periodStart, MailOwnerId owner, long inputCharacterCount) =>
        this.consumedByPeriodAndOwner[(periodStart, owner)] =
            this.consumedByPeriodAndOwner.GetValueOrDefault((periodStart, owner)) + inputCharacterCount;

    /// <inheritdoc />
    public Task<EmbeddingSpendTotals> ReadConsumedInputCharactersAsync(
        DateTimeOffset periodStart,
        MailOwnerId owner,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(new EmbeddingSpendTotals(
            this.consumedByPeriodAndOwner.GetValueOrDefault((periodStart, owner)),
            this.ConsumedInPeriod(periodStart)));
    }

    /// <inheritdoc />
    public Task<long> ReadDeploymentConsumedInputCharactersAsync(
        DateTimeOffset periodStart,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(this.ConsumedInPeriod(periodStart));
    }

    /// <inheritdoc />
    public Task RecordSpendAsync(
        IPersistenceSession session,
        DateTimeOffset periodStart,
        MailOwnerId owner,
        long inputCharacterCount,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        cancellationToken.ThrowIfCancellationRequested();

        this.Seed(periodStart, owner, inputCharacterCount);

        return Task.CompletedTask;
    }

    private long ConsumedInPeriod(DateTimeOffset periodStart) => this.consumedByPeriodAndOwner
        .Where(charge => charge.Key.PeriodStart == periodStart)
        .Sum(charge => charge.Value);
}
