// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.AttachmentText.Limits;
using MailFathom.Domain.Accounts;

namespace MailFathom.Application.Emails.AttachmentText.Administration;

/// <summary>Composes where attachment reading stands out of the coverage it has and the periods it has consumed.</summary>
/// <remarks>
/// Composed here rather than by whichever surface asks, for the reason the embedding status is: the composition is the
/// answer, and assembling it in an endpoint would leave a second caller to assemble it differently. It performs no
/// authorization of its own, because both callers are administrative use cases that have already demanded the grant
/// their own route publishes.
/// </remarks>
public sealed class AttachmentDerivationStatusReader
{
    private readonly IAttachmentDerivationCoverageReader coverageReader;
    private readonly AttachmentDerivationSpendGate spendGate;

    /// <summary>Initializes a new reader over the two sources one answer is composed from.</summary>
    /// <param name="coverageReader">Counts what has been read and what waits.</param>
    /// <param name="spendGate">Reads where each step's budget period stands.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public AttachmentDerivationStatusReader(
        IAttachmentDerivationCoverageReader coverageReader,
        AttachmentDerivationSpendGate spendGate)
    {
        ArgumentNullException.ThrowIfNull(coverageReader);
        ArgumentNullException.ThrowIfNull(spendGate);

        this.coverageReader = coverageReader;
        this.spendGate = spendGate;
    }

    /// <summary>Reads where attachment and image reading stands, for one account or for the whole deployment.</summary>
    /// <param name="account">The account to report on, or <see langword="null" /> for every account together.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>The coverage and both periods.</returns>
    /// <exception cref="OperationCanceledException">Thrown when the caller cancels.</exception>
    /// <remarks>
    /// The periods are the deployment's whichever scope the coverage was asked for, because that is what a ceiling
    /// bounds: an account is not a budget, and reporting one account's share of a deployment ceiling would invent a
    /// figure nothing enforces.
    /// </remarks>
    public async Task<AttachmentDerivationStatus> ReadAsync(
        MailAccountIdentity? account,
        CancellationToken cancellationToken) =>
        new(
            await this.coverageReader.ReadCoverageAsync(account, cancellationToken),
            await this.spendGate.ReadCurrentPeriodAsync(AttachmentDerivationStep.Extraction, cancellationToken),
            await this.spendGate.ReadCurrentPeriodAsync(AttachmentDerivationStep.Description, cancellationToken));
}
