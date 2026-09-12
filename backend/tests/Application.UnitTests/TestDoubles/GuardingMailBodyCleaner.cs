// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Cleaning;
using MailFathom.Application.SensitiveContent.Egress;

namespace MailFathom.Application.UnitTests.TestDoubles;

/// <summary>A cleaner that guards the outline before answering, the way the real producer does at its own boundary.</summary>
/// <remarks>
/// The redaction itself belongs to the adapter and is covered there. What this double is for is the half the adapter
/// cannot establish about its caller: the guard refuses a text handed to it on a flow acting for nobody, so a pass that
/// did not name the user would fail here rather than degrade, and a pass that did is the only way the recorded openings
/// come back redacted.
/// </remarks>
internal sealed class GuardingMailBodyCleaner : IMailBodyCleaner
{
    private readonly SensitiveContentEgressGuard egressGuard;

    /// <summary>Initializes the cleaner over the guard a deployment scans with.</summary>
    /// <param name="egressGuard">The guard every opening is put through before the proposal is answered.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="egressGuard" /> is <see langword="null" />.</exception>
    public GuardingMailBodyCleaner(SensitiveContentEgressGuard egressGuard)
    {
        ArgumentNullException.ThrowIfNull(egressGuard);

        this.egressGuard = egressGuard;
    }

    /// <inheritdoc />
    public bool IsActive => true;

    /// <summary>Gets the block openings as the guard left them, in the order they were asked about.</summary>
    public List<string> Guarded { get; } = [];

    /// <inheritdoc />
    public async Task<MailBodyCleaningProposal> ProposeAsync(
        CleanableMailBody body,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        var openings = await this.egressGuard.GuardAllAsync(
            SensitiveContentEgressPoint.ChatPrompt,
            [.. body.Blocks.Select(block => block.Opening)],
            cancellationToken);

        this.Guarded.AddRange(openings);

        return MailBodyCleaningProposal.Proposing(
            [new MailBodyCleaningSegment(0, body.Blocks.Count - 1, Keep: true)]);
    }
}
