// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Delivery.Governance;

/// <summary>What has already been asked to leave inside one period, counted for an account and for the deployment.</summary>
/// <param name="AccountMessageCount">The messages recorded for the account in the period.</param>
/// <param name="AccountRecipientCount">The recipients those messages name.</param>
/// <param name="DeploymentMessageCount">The messages recorded for every account in the period, the one above included.</param>
/// <param name="DeploymentRecipientCount">The recipients those messages name.</param>
/// <remarks>
/// <para>
/// Counts and nothing else. A ceiling is answered by how much a period has been asked for, so no message, address, or
/// author is describable from this — which is what lets it be read on every send and named in a refusal.
/// </para>
/// <para>
/// What is counted is what was written down rather than what was delivered. A send bounds a fault above it — a rule
/// matching more mail than expected, a caller in a loop — and such a fault produces records whether or not a submission
/// server ever accepts them, so counting deliveries would leave the ceiling counting the one thing the fault does not
/// control.
/// </para>
/// </remarks>
public readonly record struct OutgoingMailUsage(
    long AccountMessageCount,
    long AccountRecipientCount,
    long DeploymentMessageCount,
    long DeploymentRecipientCount)
{
    /// <summary>Gets the usage of a period nothing has been asked to leave in.</summary>
    public static OutgoingMailUsage None { get; } = new(
        AccountMessageCount: 0,
        AccountRecipientCount: 0,
        DeploymentMessageCount: 0,
        DeploymentRecipientCount: 0);
}
