// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Embeddings.Limits;
using MailFathom.Application.Persistence;

namespace MailFathom.Application.UnitTests.TestDoubles;

/// <summary>Keeps what each period has spent in memory, adding to a period exactly as the real upsert does.</summary>
/// <remarks>
/// Hand-written rather than substituted, because what the gate is asked and what it later writes have to agree: a
/// substitute answering the read from a script would report a period as admitting a request after the same test had
/// already spent it, and every ceiling assertion would then be about the script instead of about the ledger.
/// </remarks>
internal sealed class InMemoryEmbeddingSpendLedger : IEmbeddingSpendLedger
{
    private readonly Dictionary<DateTimeOffset, long> consumedByPeriod = [];

    /// <summary>Gets what each period has been charged so far.</summary>
    public IReadOnlyDictionary<DateTimeOffset, long> ConsumedByPeriod => this.consumedByPeriod;

    /// <summary>Charges a period before the test begins, which is how a test starts against a partly spent ceiling.</summary>
    /// <param name="periodStart">The period to charge.</param>
    /// <param name="inputCharacterCount">The characters to charge it.</param>
    public void Seed(DateTimeOffset periodStart, long inputCharacterCount) =>
        this.consumedByPeriod[periodStart] =
            this.consumedByPeriod.GetValueOrDefault(periodStart) + inputCharacterCount;

    /// <inheritdoc />
    public Task<long> ReadConsumedInputCharactersAsync(
        DateTimeOffset periodStart,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(this.consumedByPeriod.GetValueOrDefault(periodStart));
    }

    /// <inheritdoc />
    public Task RecordSpendAsync(
        IPersistenceSession session,
        DateTimeOffset periodStart,
        long inputCharacterCount,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        cancellationToken.ThrowIfCancellationRequested();

        this.Seed(periodStart, inputCharacterCount);

        return Task.CompletedTask;
    }
}
