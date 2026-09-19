// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using Microsoft.Extensions.AI.Evaluation;

namespace MailFathom.Evaluations.Discovery;

/// <summary>What one model read one question into, and what that reading was held to.</summary>
/// <param name="Model">The model under test.</param>
/// <param name="Case">The question it was asked.</param>
/// <param name="Shortfall">What its plan got wrong, or <see langword="null" /> where the plan is what the question asked for.</param>
/// <param name="Verdict">The result filed in the store, carrying the deterministic verdict and, on an ambiguous question, the judge's.</param>
internal sealed record DiscoveryPlanningOutcome(
    string Model,
    DiscoveryPlanningCase Case,
    string? Shortfall,
    EvaluationResult Verdict);
