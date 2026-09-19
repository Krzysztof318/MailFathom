// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Evaluations.StructuredAnswers;
using xRetry.v3;
using Xunit;

namespace MailFathom.Evaluations.Discovery;

/// <summary>Measures what the Discover planning agent reads each question into, under every declared model.</summary>
/// <remarks>
/// A theory over the cases rather than over the models, because the cases are written here while the models are read
/// from the run's declaration, which a theory's data would read before the skip condition. A model whose plan falls short
/// is collected rather than failing the case, so the case fails at the end naming every model that fell short and why.
/// </remarks>
public sealed class DiscoveryPlanningEvaluations
{
    /// <summary>How many times a case is run before its failure is reported.</summary>
    private const int MaxAttempts = 3;

    /// <summary>How long to wait before running it again, sized for a rate limit or a momentary overload to clear.</summary>
    private const int DelayBetweenAttemptsMs = 5000;

    /// <summary>Gets whether an evaluation run was explicitly asked for.</summary>
    /// <remarks>Public and static because that is the shape xUnit reads a skip condition from.</remarks>
    public static bool EvaluationsRequested => AiEvaluationRun.Requested;

    /// <summary>Gets every case, by the name it is filed under.</summary>
    public static TheoryData<string> Cases { get; } = new(DiscoveryPlanningCase.All.Select(static scenario => scenario.Name));

    [RetryTheory(
        MaxAttempts,
        DelayBetweenAttemptsMs,
        Skip = AiEvaluationRun.SkipReason,
        SkipUnless = nameof(EvaluationsRequested))]
    [MemberData(nameof(Cases))]
    public async Task DerivePlan_AQuestion_EveryDeclaredModelReadsItIntoThePlanItStated(string caseName)
    {
        // Arrange
        var request = DiscoveryPlanningScenario.RequestFor(DiscoveryPlanningCase.Named(caseName));

        // Act
        var shortfalls = await StructuredAnswerScenario.MeasureEveryDeclaredModelAsync(
            request,
            TestContext.Current.CancellationToken);

        // Assert
        AiEvaluationRun.AssertNoShortfalls(shortfalls);
    }
}
