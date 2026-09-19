// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using Microsoft.Extensions.AI.Evaluation;

namespace MailFathom.Evaluations.StructuredAnswers;

/// <summary>What one model answered one case with, and what that answer was held to.</summary>
/// <param name="Model">The model under test.</param>
/// <param name="Shortfall">What its answer got wrong, or <see langword="null" /> where the answer is what the case asked for.</param>
/// <param name="Verdict">The result filed in the store, carrying the deterministic verdict and, on a case that reads two ways, the judge's.</param>
internal sealed record StructuredAnswer(
    string Model,
    string? Shortfall,
    EvaluationResult Verdict);
