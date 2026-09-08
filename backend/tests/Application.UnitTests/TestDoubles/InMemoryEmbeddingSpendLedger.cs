// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Embeddings.Limits;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Access;

namespace MailFathom.Application.UnitTests.TestDoubles;

/// <summary>Keeps what each period and user has spent in memory, adding exactly as the real upsert does.</summary>
/// <remarks>
/// Hand-written rather than substituted, because what the gate is asked and what it later writes have to agree: a
/// substitute answering the read from a script would report a period as admitting a request after the same test had
/// already spent it, and every ceiling assertion would then be about the script instead of about the ledger. It keys by
/// period and user together for the same reason the table does — a fake that summed both users into one figure would
/// let a per-user ceiling pass a test it does not enforce.
/// </remarks>
internal sealed class InMemoryEmbeddingSpendLedger : IEmbeddingSpendLedger
{
    private readonly Dictionary<(DateTimeOffset PeriodStart, MailUserId User), long> consumedByPeriodAndUser = [];

    /// <summary>Gets what each period and user has been charged so far.</summary>
    public IReadOnlyDictionary<(DateTimeOffset PeriodStart, MailUserId User), long> ConsumedByPeriodAndUser =>
        this.consumedByPeriodAndUser;

    /// <summary>Gets what each period has been charged across every user.</summary>
    public IReadOnlyDictionary<DateTimeOffset, long> ConsumedByPeriod => this.consumedByPeriodAndUser
        .GroupBy(charge => charge.Key.PeriodStart)
        .ToDictionary(period => period.Key, period => period.Sum(charge => charge.Value));

    /// <summary>Charges a period before the test begins, which is how a test starts against a partly spent ceiling.</summary>
    /// <param name="periodStart">The period to charge.</param>
    /// <param name="user">The user the spend is attributed to.</param>
    /// <param name="inputCharacterCount">The characters to charge it.</param>
    public void Seed(DateTimeOffset periodStart, MailUserId user, long inputCharacterCount) =>
        this.consumedByPeriodAndUser[(periodStart, user)] =
            this.consumedByPeriodAndUser.GetValueOrDefault((periodStart, user)) + inputCharacterCount;

    /// <inheritdoc />
    public Task<EmbeddingSpendTotals> ReadConsumedInputCharactersAsync(
        DateTimeOffset periodStart,
        MailUserId user,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(new EmbeddingSpendTotals(
            this.consumedByPeriodAndUser.GetValueOrDefault((periodStart, user)),
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
        MailUserId user,
        long inputCharacterCount,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        cancellationToken.ThrowIfCancellationRequested();

        this.Seed(periodStart, user, inputCharacterCount);

        return Task.CompletedTask;
    }

    private long ConsumedInPeriod(DateTimeOffset periodStart) => this.consumedByPeriodAndUser
        .Where(charge => charge.Key.PeriodStart == periodStart)
        .Sum(charge => charge.Value);
}
