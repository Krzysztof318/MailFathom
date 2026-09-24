// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.Chat;
using MailFathom.Evaluations.StructuredAnswers;
using Xunit;

namespace MailFathom.Evaluations.ThreadStates;

/// <summary>Measures where the thread-state agent says each corpus conversation stands, under every declared model.</summary>
/// <remarks>A theory over the cases for the reason <c>DiscoveryPlanningEvaluations</c> gives.</remarks>
public sealed class ThreadStateEvaluations
{
    /// <summary>Gets whether an evaluation run was explicitly asked for.</summary>
    /// <remarks>Public and static because that is the shape xUnit reads a skip condition from.</remarks>
    public static bool EvaluationsRequested => AiEvaluationRun.Requested;

    /// <summary>Gets every case, by the name it is filed under.</summary>
    public static TheoryData<string> Cases { get; } = new(ThreadStateCase.All.Select(static scenario => scenario.Name));

    [Theory(Skip = AiEvaluationRun.SkipReason, SkipUnless = nameof(EvaluationsRequested))]
    [MemberData(nameof(Cases))]
    public async Task DeriveAsync_ACorpusConversation_EveryDeclaredModelStatesWhereItStands(string caseName)
    {
        // Arrange
        var request = ThreadStateScenario.RequestFor(ThreadStateCase.Named(caseName));

        // Act
        var shortfalls = await StructuredAnswerScenario.MeasureEveryDeclaredModelAsync(
            ChatCapability.ThreadState,
            request,
            TestContext.Current.CancellationToken);

        // Assert
        AiEvaluationRun.AssertNoShortfalls(shortfalls);
    }
}
