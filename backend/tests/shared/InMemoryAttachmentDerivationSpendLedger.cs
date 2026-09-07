// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.AttachmentText.Limits;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Access;

namespace MailFathom.TestSupport;

/// <summary>Keeps what each period, step, and owner has consumed in memory, adding exactly as the real upsert does.</summary>
/// <remarks>
/// Hand-written for the reason the embedding spend ledger double beside it is, and keyed by the step as well as by the
/// period and the owner: the two steps count in units that do not convert, so a fake summing octets read into
/// description calls would let either ceiling pass a test that never enforced it.
/// </remarks>
internal sealed class InMemoryAttachmentDerivationSpendLedger : IAttachmentDerivationSpendLedger
{
    private readonly Dictionary<(DateTimeOffset PeriodStart, AttachmentDerivationStep Step, MailOwnerId Owner), long> consumed = [];

    /// <summary>Gets what each period, step, and owner has been charged so far.</summary>
    public IReadOnlyDictionary<(DateTimeOffset PeriodStart, AttachmentDerivationStep Step, MailOwnerId Owner), long> Consumed =>
        this.consumed;

    /// <summary>Charges a period before the test begins, which is how a test starts against a partly spent ceiling.</summary>
    /// <param name="periodStart">The period to charge.</param>
    /// <param name="derivationStep">The step the units belong to.</param>
    /// <param name="owner">The owner the spend is attributed to.</param>
    /// <param name="unitCount">The units to charge it, in that step's own unit.</param>
    public void Seed(
        DateTimeOffset periodStart,
        AttachmentDerivationStep derivationStep,
        MailOwnerId owner,
        long unitCount) =>
        this.consumed[(periodStart, derivationStep, owner)] =
            this.consumed.GetValueOrDefault((periodStart, derivationStep, owner)) + unitCount;

    /// <inheritdoc />
    public Task<AttachmentDerivationTotals> ReadConsumedAsync(
        DateTimeOffset periodStart,
        AttachmentDerivationStep derivationStep,
        MailOwnerId owner,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(new AttachmentDerivationTotals(
            this.consumed.GetValueOrDefault((periodStart, derivationStep, owner)),
            this.ConsumedInPeriod(periodStart, derivationStep)));
    }

    /// <inheritdoc />
    public Task<long> ReadDeploymentConsumedAsync(
        DateTimeOffset periodStart,
        AttachmentDerivationStep derivationStep,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(this.ConsumedInPeriod(periodStart, derivationStep));
    }

    /// <inheritdoc />
    public Task RecordSpendAsync(
        IPersistenceSession session,
        DateTimeOffset periodStart,
        AttachmentDerivationStep derivationStep,
        MailOwnerId owner,
        long unitCount,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentOutOfRangeException.ThrowIfNegative(unitCount);
        cancellationToken.ThrowIfCancellationRequested();

        if (unitCount == 0)
        {
            return Task.CompletedTask;
        }

        this.Seed(periodStart, derivationStep, owner, unitCount);

        return Task.CompletedTask;
    }

    private long ConsumedInPeriod(DateTimeOffset periodStart, AttachmentDerivationStep derivationStep) => this.consumed
        .Where(charge => charge.Key.PeriodStart == periodStart && charge.Key.Step == derivationStep)
        .Sum(charge => charge.Value);
}
