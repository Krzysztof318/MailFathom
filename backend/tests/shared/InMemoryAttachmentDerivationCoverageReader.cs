// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.AttachmentText.Administration;
using MailFathom.Application.Emails.AttachmentText.Limits;
using MailFathom.Domain.Accounts;
using Microsoft.Extensions.Time.Testing;

namespace MailFathom.TestSupport;

/// <summary>Answers with whatever coverage a test placed on it, for every account and for the deployment alike.</summary>
/// <remarks>
/// Coverage is a database aggregate rather than a decision, so a test about a surface that reports it has nothing to
/// gain from recomputing one: what it needs is a figure it chose. It records which account each read was scoped to, so
/// a test can still prove that the mailbox surface asked per account rather than once for the deployment.
/// </remarks>
internal sealed class InMemoryAttachmentDerivationCoverageReader : IAttachmentDerivationCoverageReader
{
    private readonly List<MailAccountIdentity?> reads = [];

    /// <summary>Gets or sets what every read answers with.</summary>
    public AttachmentDerivationCoverage Coverage { get; set; } = AttachmentDerivationCoverage.Nothing;

    /// <summary>Gets the account each read was scoped to, in the order the reads happened.</summary>
    public IReadOnlyList<MailAccountIdentity?> Reads => this.reads;

    /// <summary>Composes a status reader over this coverage and a budget that declares no ceiling.</summary>
    /// <param name="now">The instant the periods are anchored to.</param>
    /// <returns>The reader, and this coverage source behind it.</returns>
    public static (InMemoryAttachmentDerivationCoverageReader Coverage, AttachmentDerivationStatusReader Reader) Unbounded(
        DateTimeOffset now) =>
        Bounded(now, AttachmentDerivationBudget.Unbounded);

    /// <summary>Composes a status reader over this coverage and whichever ceilings a test declares.</summary>
    /// <param name="now">The instant the periods are anchored to.</param>
    /// <param name="budget">The ceilings the periods are read against.</param>
    /// <returns>The reader, and this coverage source behind it.</returns>
    public static (InMemoryAttachmentDerivationCoverageReader Coverage, AttachmentDerivationStatusReader Reader) Bounded(
        DateTimeOffset now,
        AttachmentDerivationBudget budget)
    {
        InMemoryAttachmentDerivationCoverageReader coverage = new();

        return (coverage, new AttachmentDerivationStatusReader(
            coverage,
            new AttachmentDerivationSpendGate(
                new InMemoryAttachmentDerivationSpendLedger(),
                budget,
                new FakeTimeProvider(now))));
    }

    /// <inheritdoc />
    public Task<AttachmentDerivationCoverage> ReadCoverageAsync(
        MailAccountIdentity? account,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        this.reads.Add(account);

        return Task.FromResult(this.Coverage);
    }
}
