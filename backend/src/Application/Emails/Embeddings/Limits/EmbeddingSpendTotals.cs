// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Emails.Embeddings.Limits;

/// <summary>What one budget period has been charged, for one owner and for the deployment they are part of.</summary>
/// <param name="OwnerConsumedInputCharacterCount">The characters sent inside this period for the named owner.</param>
/// <param name="DeploymentConsumedInputCharacterCount">The characters sent inside this period for every owner together.</param>
/// <remarks>
/// The two figures are answered together rather than by two reads, because they are two aggregations of one set of
/// rows and a gate that read them separately could admit a request against an owner total and a deployment total taken
/// at different moments. The owner's figure is never above the deployment's, which is what makes a refusal
/// attributable: reaching the deployment bound without reaching the owner's is somebody else's spending.
/// </remarks>
public sealed record EmbeddingSpendTotals(
    long OwnerConsumedInputCharacterCount,
    long DeploymentConsumedInputCharacterCount)
{
    /// <summary>Gets the totals of a period nothing has been charged to yet.</summary>
    public static EmbeddingSpendTotals Unspent { get; } = new(0, 0);
}
