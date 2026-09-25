// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Evaluations.Judging;
using MailFathom.Evaluations.UnitTests.Enrichment;
using Microsoft.Extensions.AI;
using OpenAI.Chat;
using Xunit;

namespace MailFathom.Evaluations.UnitTests.Judging;

public sealed class JudgeDeclarationTests
{
    private const string JudgeModel = "planted-judge-model-4c1e";
    private const string JudgeApiKey = "planted-judge-key-9b27";
    /// <summary>What every quality evaluator asks a verdict to fit in.</summary>
    private const int EvaluatorOutputTokens = 800;

    private static readonly Uri JudgeAddress = new("https://planted-judge-host.invalid/v1/");

    [Fact]
    public async Task Present_WithNoDeclaredEffort_LeavesTheRequestAsTheEvaluatorComposedIt()
    {
        // Arrange
        var declaration = JudgeDeclaration.Of(JudgeAddress, JudgeModel, JudgeApiKey, reasoningEffort: null);
        using var provider = JudgeProvider();
        using var judge = declaration.Present(provider);

        // Act
        await judge.GetResponseAsync(
            "Grade this.",
            new ChatOptions { MaxOutputTokens = EvaluatorOutputTokens },
            TestContext.Current.CancellationToken);

        // Assert
        var received = Assert.Single(provider.ReceivedOptions);
        Assert.Null(received?.RawRepresentationFactory);
        Assert.Equal(EvaluatorOutputTokens, received?.MaxOutputTokens);
    }

    [Fact]
    public async Task Present_WithADeclaredEffort_StatesItOnEveryCall()
    {
        // Arrange
        var declaration = JudgeDeclaration.Of(JudgeAddress, JudgeModel, JudgeApiKey, reasoningEffort: "low");
        using var provider = JudgeProvider();
        using var judge = declaration.Present(provider);

        // Act
        await judge.GetResponseAsync("Grade this.", new ChatOptions(), TestContext.Current.CancellationToken);
        await judge.GetResponseAsync("Grade that.", options: null, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(["low", "low"], provider.ReceivedOptions.Select(StatedEffort));
    }

    [Fact]
    public async Task Present_WithADeclaredEffort_LeavesTheVerdictRoomBesideTheReasoning()
    {
        // Arrange
        var declaration = JudgeDeclaration.Of(JudgeAddress, JudgeModel, JudgeApiKey, reasoningEffort: "xhigh");
        using var provider = JudgeProvider();
        using var judge = declaration.Present(provider);

        // Act
        await judge.GetResponseAsync(
            "Grade this.",
            new ChatOptions { MaxOutputTokens = EvaluatorOutputTokens },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(Assert.Single(provider.ReceivedOptions)?.MaxOutputTokens >= EvaluatorOutputTokens / (1 - 0.95));
    }

    [Fact]
    public void CachingKey_AtADifferentEffort_FilesTheVerdictsApart()
    {
        // Arrange
        string?[] efforts = [null, "low", "high"];

        // Act
        var keys = efforts.Select(effort => JudgeDeclaration.Of(JudgeAddress, JudgeModel, JudgeApiKey, effort).CachingKey).ToArray();

        // Assert
        Assert.Equal(efforts.Length, keys.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Of_AnEffortNoProviderCouldReadAsALevel_IsRefusedNamingTheVariableAndNeverTheValue()
    {
        // Arrange
        const string Effort = "planted-effort\n";

        // Act
        var refusal = Assert.Throws<InvalidOperationException>(
            () => JudgeDeclaration.Of(JudgeAddress, JudgeModel, JudgeApiKey, Effort));

        // Assert
        Assert.Contains("MAILFATHOM_JUDGE_REASONING_EFFORT", refusal.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("planted-effort", refusal.Message, StringComparison.Ordinal);
    }

    private static ScriptedChatClient JudgeProvider() =>
        new("<S0>Supported.</S0>", new ChatClientMetadata("planted-provider", JudgeAddress, JudgeModel));

    // The chat completions reasoning member carries the evaluation-only marker in this release of the client library,
    // and reading it back is the only way to see the effort the request will carry.
#pragma warning disable OPENAI001
    private static string? StatedEffort(ChatOptions? options) =>
        (options?.RawRepresentationFactory?.Invoke(null!) as ChatCompletionOptions)?.ReasoningEffortLevel?.ToString();
#pragma warning restore OPENAI001
}
