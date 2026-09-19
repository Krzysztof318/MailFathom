// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.Chat;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;

namespace MailFathom.Evaluations.StructuredAnswers;

/// <summary>One case put to an agent that answers with a structure, and how its answer is held to the case.</summary>
/// <param name="ScenarioName">The name the case is filed and cached under.</param>
/// <param name="Instruction">The instruction the agent is composed with, which the judge grades the answer against.</param>
/// <param name="Turn">The turn a deployment composes for the case.</param>
/// <param name="Compose">Composes the agent over the model under test, through the composition a deployment uses.</param>
/// <param name="Shortfall">Reads the answer the way a deployment reads it and names what it gets wrong, or answers <see langword="null" /> when it gets nothing wrong.</param>
/// <param name="ExpectationMetricName">The name the deterministic verdict is recorded under.</param>
/// <param name="Evaluators">What the judge is asked on this case, which is nothing unless the case reads two ways.</param>
internal sealed record StructuredAnswerRequest(
    string ScenarioName,
    string Instruction,
    string Turn,
    Func<IChatClient, ChatGenerationPlan, ChatClientAgent> Compose,
    Func<string, string?> Shortfall,
    string ExpectationMetricName,
    IReadOnlyList<IEvaluator> Evaluators);
