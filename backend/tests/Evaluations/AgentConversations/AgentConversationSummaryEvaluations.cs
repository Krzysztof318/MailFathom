// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.Chat;
using MailFathom.Evaluations.StructuredAnswers;
using Xunit;

namespace MailFathom.Evaluations.AgentConversations;

/// <summary>Measures what the compaction agent keeps of a conversation, under every declared model.</summary>
/// <remarks>A theory over the cases for the reason <c>DiscoveryPlanningEvaluations</c> gives.</remarks>
public sealed class AgentConversationSummaryEvaluations
{
    /// <summary>Gets whether an evaluation run was explicitly asked for.</summary>
    /// <remarks>Public and static because that is the shape xUnit reads a skip condition from.</remarks>
    public static bool EvaluationsRequested => AiEvaluationRun.Requested;

    /// <summary>Gets every case, by the name it is filed under.</summary>
    public static TheoryData<string> Cases { get; } = new(AgentConversationSummaryCase.All.Select(static scenario => scenario.Name));

    [Theory(Skip = AiEvaluationRun.SkipReason, SkipUnless = nameof(EvaluationsRequested))]
    [MemberData(nameof(Cases))]
    public async Task SummarizeAsync_AStretchOfConversation_EveryDeclaredModelKeepsWhatAFollowUpNeeds(string caseName)
    {
        // Arrange
        var request = AgentConversationSummaryScenario.RequestFor(AgentConversationSummaryCase.Named(caseName));

        // Act
        var shortfalls = await StructuredAnswerScenario.MeasureEveryDeclaredModelAsync(
            ChatCapability.ConversationCompaction,
            request,
            TestContext.Current.CancellationToken);

        // Assert
        AiEvaluationRun.AssertNoShortfalls(shortfalls);
    }
}
