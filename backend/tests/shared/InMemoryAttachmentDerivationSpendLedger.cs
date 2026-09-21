// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.AttachmentText.Limits;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Access;

namespace MailFathom.TestSupport;

/// <summary>Keeps what each period, step, and user has consumed in memory, adding exactly as the real upsert does.</summary>
/// <remarks>
/// Hand-written for the reason the embedding spend ledger double beside it is, and keyed by the step as well as by the
/// period and the user: the two steps count in units that do not convert, so a fake summing octets read into
/// description calls would let either ceiling pass a test that never enforced it. The deployment's own total is kept
/// under the user identity that names nobody, exactly as the table keeps it, so a test can tell what was read from
/// what was charged.
/// </remarks>
internal sealed class InMemoryAttachmentDerivationSpendLedger : IAttachmentDerivationSpendLedger
{
    private readonly Dictionary<(DateTimeOffset PeriodStart, AttachmentDerivationStep Step, UserId User), long> consumed = [];

    /// <summary>Gets what each period, step, and user has been charged so far.</summary>
    public IReadOnlyDictionary<(DateTimeOffset PeriodStart, AttachmentDerivationStep Step, UserId User), long> Consumed =>
        this.consumed;

    /// <summary>Charges a period before the test begins, which is how a test starts against a partly spent ceiling.</summary>
    /// <param name="periodStart">The period to charge.</param>
    /// <param name="derivationStep">The step the units belong to.</param>
    /// <param name="user">The user the spend is attributed to.</param>
    /// <param name="unitCount">The units to charge it, in that step's own unit.</param>
    public void Seed(
        DateTimeOffset periodStart,
        AttachmentDerivationStep derivationStep,
        UserId user,
        long unitCount) =>
        this.consumed[(periodStart, derivationStep, user)] =
            this.consumed.GetValueOrDefault((periodStart, derivationStep, user)) + unitCount;

    /// <summary>Charges a period's deployment total, which is what was read rather than what a user was charged.</summary>
    /// <param name="periodStart">The period to charge.</param>
    /// <param name="derivationStep">The step the units belong to.</param>
    /// <param name="unitCount">The units to charge it, in that step's own unit.</param>
    public void SeedDeployment(DateTimeOffset periodStart, AttachmentDerivationStep derivationStep, long unitCount) =>
        this.Seed(periodStart, derivationStep, default, unitCount);

    /// <inheritdoc />
    public Task<AttachmentDerivationTotals> ReadConsumedAsync(
        DateTimeOffset periodStart,
        AttachmentDerivationStep derivationStep,
        UserId user,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(new AttachmentDerivationTotals(
            this.consumed.GetValueOrDefault((periodStart, derivationStep, user)),
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
        IReadOnlyCollection<UserId> users,
        long unitCount,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(users);
        ArgumentOutOfRangeException.ThrowIfNegative(unitCount);
        cancellationToken.ThrowIfCancellationRequested();

        if (unitCount == 0)
        {
            return Task.CompletedTask;
        }

        this.Seed(periodStart, derivationStep, default, unitCount);

        foreach (var user in users)
        {
            this.Seed(periodStart, derivationStep, user, unitCount);
        }

        return Task.CompletedTask;
    }

    private long ConsumedInPeriod(DateTimeOffset periodStart, AttachmentDerivationStep derivationStep) =>
        this.consumed.GetValueOrDefault((periodStart, derivationStep, default));
}
