// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net;
using System.Net.Http.Json;
using System.Text;
using MailFathom.Evaluations.Costing;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Evaluations.UnitTests.Costing;

/// <summary>Proves what a run reports spending is what the provider said it charged, and nothing where it said nothing.</summary>
/// <remarks>
/// Free: the answers are scripted through <see cref="FakeHttpMessageHandler" />, so no provider is reached and every run
/// of this project proves it. The shape they are written in is OpenRouter's, which reports <c>usage.cost</c> in credits
/// on every answer.
/// </remarks>
public sealed class ProviderSpendHandlerTests
{
    private static readonly Uri Completions = new("https://provider.invalid/v1/chat/completions");

    /// <summary>An answer a cut connection leaves behind: the content type says JSON and the body stops mid-property.</summary>
    private const string TruncatedAnswer = """{"usage": {"prompt_toke""";

    [Fact]
    public async Task SendAsync_AnAnswerCarryingACharge_RecordsWhatTheProviderCharged()
    {
        // Arrange
        var meter = new SpendMeter();
        using var provider = Answering(new { usage = new { prompt_tokens = 1_200, completion_tokens = 340, cost = 0.0123m } });
        using var metering = new ProviderSpendHandler(meter, provider);
        using var client = new HttpClient(metering, disposeHandler: false);

        // Act
        using var answer = await AskAsync(client);

        // Assert
        Assert.Equal(new PaidUsage(1, 1_200, 340, 0.0123m), meter.Take());
    }

    [Fact]
    public async Task SendAsync_SeveralAnswers_AddsUpWhatTheyAllCost()
    {
        // Arrange
        var meter = new SpendMeter();
        using var provider = Answering(new { usage = new { prompt_tokens = 10, completion_tokens = 5, cost = 0.5m } });
        using var metering = new ProviderSpendHandler(meter, provider);
        using var client = new HttpClient(metering, disposeHandler: false);

        // Act
        using var first = await AskAsync(client);
        using var second = await AskAsync(client);

        // Assert
        Assert.Equal(new PaidUsage(2, 20, 10, 1.0m), meter.Take());
    }

    [Fact]
    public async Task SendAsync_AnAnswerStatingNoCharge_CountsTheCallAndLeavesTheChargeUnstated()
    {
        // Arrange
        var meter = new SpendMeter();
        using var provider = Answering(new { usage = new { prompt_tokens = 7, completion_tokens = 3 } });
        using var metering = new ProviderSpendHandler(meter, provider);
        using var client = new HttpClient(metering, disposeHandler: false);

        // Act
        using var answer = await AskAsync(client);

        // Assert
        Assert.Equal(new PaidUsage(1, 7, 3, null), meter.Take());
    }

    [Fact]
    public async Task SendAsync_AnAnswerThatClaimsToBeJsonAndIsNot_RecordsNothingAndAnswersTheCallerAnyway()
    {
        // Arrange
        var meter = new SpendMeter();
        using var provider = new FakeHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(TruncatedAnswer, Encoding.UTF8, "application/json"),
        }));

        using var metering = new ProviderSpendHandler(meter, provider);
        using var client = new HttpClient(metering, disposeHandler: false);

        // Act
        using var answer = await AskAsync(client);

        // Assert
        Assert.Equal((HttpStatusCode.OK, new PaidUsage(0, 0, 0, null)), (answer.StatusCode, meter.Take()));
    }

    [Fact]
    public async Task SendAsync_ARefusedRequest_RecordsNothingSpent()
    {
        // Arrange
        var meter = new SpendMeter();
        using var provider = new FakeHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests)));
        using var metering = new ProviderSpendHandler(meter, provider);
        using var client = new HttpClient(metering, disposeHandler: false);

        // Act
        using var answer = await AskAsync(client);

        // Assert
        Assert.Equal(new PaidUsage(0, 0, 0, null), meter.Take());
    }

    [Fact]
    public async Task SendAsync_AnAnswerItReadTheUsageOf_LeavesTheBodyReadableByTheClient()
    {
        // Arrange
        var meter = new SpendMeter();
        using var provider = Answering(new { usage = new { prompt_tokens = 1, completion_tokens = 1, cost = 0.25m } });
        using var metering = new ProviderSpendHandler(meter, provider);
        using var client = new HttpClient(metering, disposeHandler: false);

        // Act
        using var answer = await AskAsync(client);

        // Assert
        Assert.Contains(
            "\"cost\":0.25",
            await answer.Content.ReadAsStringAsync(TestContext.Current.CancellationToken),
            StringComparison.Ordinal);
    }

    private static FakeHttpMessageHandler Answering(object answer) =>
        new((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(answer),
        }));

    private static async Task<HttpResponseMessage> AskAsync(HttpClient client)
    {
        using var request = JsonContent.Create(new { });

        return await client.PostAsync(Completions, request, TestContext.Current.CancellationToken);
    }
}
