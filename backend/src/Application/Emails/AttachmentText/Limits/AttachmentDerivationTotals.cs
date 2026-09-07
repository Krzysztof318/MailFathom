// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Emails.AttachmentText.Limits;

/// <summary>What one budget period has been charged for one step, for one owner and for the deployment they are part of.</summary>
/// <param name="OwnerConsumedUnitCount">What the named owner consumed inside this period, in the step's own unit.</param>
/// <param name="DeploymentConsumedUnitCount">What every owner together consumed inside it.</param>
/// <remarks>
/// The two figures are answered together rather than by two reads, because they are two aggregations of one set of rows
/// and a gate that read them separately could admit work against an owner total and a deployment total taken at
/// different moments. The owner's figure is never above the deployment's, which is what makes a refusal attributable:
/// reaching the deployment bound without reaching the owner's is somebody else's mail being read.
/// </remarks>
public sealed record AttachmentDerivationTotals(
    long OwnerConsumedUnitCount,
    long DeploymentConsumedUnitCount)
{
    /// <summary>Gets the totals of a period nothing has been charged to yet.</summary>
    public static AttachmentDerivationTotals Unspent { get; } = new(0, 0);
}
