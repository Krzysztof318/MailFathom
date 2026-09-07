// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Emails.AttachmentText.Limits;

/// <summary>Which of the two ceilings on one step of attachment reading a period reached.</summary>
/// <remarks>
/// The same distinction <c>EmbeddingSpendBound</c> draws, and for the same reason: the two refusals need different
/// actions. One is answered by raising the deployment's ceiling or by waiting for the period to roll over, the other by
/// raising that owner's ceiling or by leaving them to wait while everybody else's mail keeps being read.
/// </remarks>
public enum AttachmentDerivationBound
{
    /// <summary>Neither ceiling is reached, so the work may be taken.</summary>
    None = 0,

    /// <summary>The named owner has consumed what one period admits for them, while the deployment still has room.</summary>
    Owner = 1,

    /// <summary>The deployment has consumed what one period admits in total, whatever any one owner has left.</summary>
    /// <remarks>
    /// Reported in preference to <see cref="Owner" /> when both are reached, because it is the wider fact: raising an
    /// owner's ceiling would change nothing while the instance itself is at its budget.
    /// </remarks>
    Deployment = 2,
}
