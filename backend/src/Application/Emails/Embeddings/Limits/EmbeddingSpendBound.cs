// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Emails.Embeddings.Limits;

/// <summary>Which of the two embedding spend ceilings a period reached.</summary>
/// <remarks>
/// A deployment bounds what it will spend in total and what any one owner may spend of that, and the two refusals need
/// different actions: one is raised by raising the deployment's ceiling or waiting, the other by raising that owner's
/// or by leaving them to wait while everybody else keeps working. Naming which was reached is what lets an operator
/// tell "this instance is at its budget" from "this person is at theirs".
/// </remarks>
public enum EmbeddingSpendBound
{
    /// <summary>Neither ceiling is reached, so the request may be sent.</summary>
    None = 0,

    /// <summary>The named owner has spent what one period admits for them, while the deployment still has room.</summary>
    Owner = 1,

    /// <summary>The deployment has spent what one period admits in total, whatever any one owner has left.</summary>
    /// <remarks>
    /// Reported in preference to <see cref="Owner" /> when both are reached, because it is the wider fact: raising an
    /// owner's ceiling would change nothing while the instance itself is at its budget.
    /// </remarks>
    Deployment = 2,
}
