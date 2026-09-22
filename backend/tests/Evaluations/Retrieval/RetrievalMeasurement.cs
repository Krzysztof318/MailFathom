// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using Microsoft.Extensions.AI.Evaluation;

namespace MailFathom.Evaluations.Retrieval;

/// <summary>Where every case's evidence landed under one model, handed to the evaluator that turns it into metrics.</summary>
internal sealed class RetrievalMeasurement : EvaluationContext
{
    /// <summary>The name the context is filed under.</summary>
    public const string ContextName = "Evidence ranks";

    /// <summary>Initializes a new instance of the <see cref="RetrievalMeasurement" /> class.</summary>
    /// <param name="cases">Where each case's evidence landed.</param>
    public RetrievalMeasurement(IReadOnlyList<RetrievalCaseRanks> cases)
        : this(cases, Describe(cases))
    {
    }

    private RetrievalMeasurement(IReadOnlyList<RetrievalCaseRanks> cases, string summary)
        : base(ContextName, summary)
    {
        this.Cases = cases;
        this.Summary = summary;
    }

    /// <summary>Gets where each case's evidence landed.</summary>
    public IReadOnlyList<RetrievalCaseRanks> Cases { get; }

    /// <summary>Gets one line per case naming each piece of evidence's rank, which is what the report shows as the answer.</summary>
    public string Summary { get; }

    private static string Describe(IReadOnlyList<RetrievalCaseRanks> cases) =>
        string.Join(
            '\n',
            cases.Select(static ranks => string.Create(
                CultureInfo.InvariantCulture,
                $"{ranks.CaseName}: {string.Join(", ", ranks.EvidenceRanks.Select(static rank => rank?.ToString(CultureInfo.InvariantCulture) ?? "unranked"))}")));
}
