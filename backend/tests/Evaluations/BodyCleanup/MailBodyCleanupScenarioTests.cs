// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Evaluations.StructuredAnswers;
using Microsoft.Extensions.AI.Evaluation;
using Xunit;

namespace MailFathom.Evaluations.BodyCleanup;

/// <summary>Proves, without calling any provider, that each case describes its body and holds a partition to keeping all of it.</summary>
/// <remarks>
/// A partition names blocks by number, so every answer here is written against the outline the case's own body produces
/// rather than against a count copied out of the corpus.
/// </remarks>
public sealed class MailBodyCleanupScenarioTests : IDisposable
{
    private readonly ScriptedStructuredAnswerRun run = new();

    public static TheoryData<string> Cases { get; } = new(MailBodyCleanupCase.All.Select(static scenario => scenario.Name));

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task DescribeAsync_ACase_OutlinesTheBlockAModelIsTemptedToDrop(string caseName)
    {
        // Arrange
        var scenario = MailBodyCleanupCase.Named(caseName);

        // Act
        var outline = await scenario.DescribeAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(outline.Blocks.Skip(1), block => block.Opening.Contains(scenario.Tempting, StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task RunAsync_OneRangeKeepingEveryBlock_RecordsTheBodyAsKept(string caseName)
    {
        // Arrange
        var (request, blocks) = await RequestAsync(caseName);
        using var model = ScriptedStructuredAnswerRun.Model($$"""{"segments":[{"from":0,"to":{{blocks - 1}},"action":"keep"}]}""");

        // Act
        var outcome = await this.run.RunAsync(request, model);

        // Assert
        var metric = outcome.Verdict.Get<BooleanMetric>(MailBodyCleanupScenario.ExpectationMetricName);

        Assert.Equal((true, true), (metric.Value, outcome.Shortfall is null));
        Assert.Equal(0, this.run.Judge.Requests);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task RunAsync_APartitionDroppingTheTemptingBlock_NamesTheBlockItDropped(string caseName)
    {
        // Arrange
        var scenario = MailBodyCleanupCase.Named(caseName);
        var outline = await scenario.DescribeAsync(TestContext.Current.CancellationToken);
        var tempting = outline.Blocks.Last(block => block.Opening.Contains(scenario.Tempting, StringComparison.Ordinal)).Index;
        var last = outline.Blocks.Count - 1;

        string[] ranges =
        [
            .. tempting > 0 ? [$$"""{"from":0,"to":{{tempting - 1}},"action":"keep"}"""] : Array.Empty<string>(),
            $$"""{"from":{{tempting}},"to":{{tempting}},"action":"drop"}""",
            .. tempting < last ? [$$"""{"from":{{tempting + 1}},"to":{{last}},"action":"keep"}"""] : Array.Empty<string>(),
        ];

        using var model = ScriptedStructuredAnswerRun.Model($$"""{"segments":[{{string.Join(',', ranges)}}]}""");

        // Act
        var outcome = await this.run.RunAsync(
            await MailBodyCleanupScenario.RequestForAsync(scenario, TestContext.Current.CancellationToken),
            model);

        // Assert
        Assert.Contains(scenario.Tempting, outcome.Shortfall, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task RunAsync_RangesLeavingTheLastBlockUnnamed_RecordsAPartitionThePaneRefuses(string caseName)
    {
        // Arrange
        var (request, blocks) = await RequestAsync(caseName);
        using var model = ScriptedStructuredAnswerRun.Model($$"""{"segments":[{"from":0,"to":{{blocks - 2}},"action":"keep"}]}""");

        // Act
        var outcome = await this.run.RunAsync(request, model);

        // Assert
        Assert.Contains("do not partition", outcome.Shortfall, StringComparison.Ordinal);
    }

    public void Dispose() => this.run.Dispose();

    private static async Task<(StructuredAnswerRequest Request, int Blocks)> RequestAsync(string caseName)
    {
        var scenario = MailBodyCleanupCase.Named(caseName);
        var outline = await scenario.DescribeAsync(TestContext.Current.CancellationToken);

        return (await MailBodyCleanupScenario.RequestForAsync(scenario, TestContext.Current.CancellationToken), outline.Blocks.Count);
    }
}
