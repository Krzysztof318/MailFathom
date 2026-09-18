// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Enrichment;
using Microsoft.Extensions.AI.Evaluation;

namespace MailFathom.Evaluations.Enrichment;

/// <summary>What one model made of the scenario's message, and what the judge said about it.</summary>
/// <param name="Model">The model under test.</param>
/// <param name="Marks">The marks its answer produced once read the way a deployment reads it.</param>
/// <param name="Verdict">The judge's verdict on those marks.</param>
internal sealed record EmailEnrichmentOutcome(
    string Model,
    IReadOnlyList<EmailEnrichmentMark> Marks,
    EvaluationResult Verdict);
