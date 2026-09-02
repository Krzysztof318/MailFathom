// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Retrieval.AskMail;

/// <summary>Which of the two spend ceilings a refused question reached.</summary>
/// <remarks>
/// They differ in what waiting buys. A period that is spent turns over, so the same question asked later is answered;
/// a run that is spent was one question that grew past what one question may cost, and asking it again reaches the same
/// ceiling by the same route. A caller that could not tell them apart would retry the one where retrying is futile.
/// </remarks>
public enum MailAnsweringBudgetScope
{
    /// <summary>One run reached what a single question may spend.</summary>
    Run = 0,

    /// <summary>The runs of the current period have between them reached what a period may spend.</summary>
    Period = 1,
}
