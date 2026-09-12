// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.Chat;
using MailFathom.AI.UnitTests.TestDoubles;
using MailFathom.Application.Chat;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.AI.UnitTests.Chat;

/// <summary>Covers the one place the fall-through rule lives, which every chat call site runs its attempt through.</summary>
/// <remarks>
/// What matters here is which failures are worth a second model and which are not: a rule that differed between the pass
/// a reader waits for and the run that answers a question would be a deployment paying twice for one refusal.
/// </remarks>
public sealed class ChatModelFallThroughTests
{
    [Fact]
    public async Task RunAsync_AModelThatAnswers_NeverReachesTheFallback()
    {
        // Arrange
        var asked = new List<string>();
        using var logs = new RecordingLoggerProvider();
        var logger = logs.CreateLogger("chat");

        // Act
        var answer = await ChatModelFallThrough.RunAsync(
            PlanWithFallback(),
            logger,
            (model, _) =>
            {
                asked.Add(model.Endpoint.Alias);

                return Task.FromResult("an answer");
            },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("an answer", answer);
        Assert.Equal(["answering"], asked);
    }

    /// <summary>The whole point of the fallback: an endpoint that could not be reached is a statement about that endpoint, and the second one is a different address under a different credential.</summary>
    [Theory]
    [InlineData(ChatGenerationFailure.CredentialRejected)]
    [InlineData(ChatGenerationFailure.RateLimited)]
    [InlineData(ChatGenerationFailure.RequestTimedOut)]
    [InlineData(ChatGenerationFailure.TransportFaulted)]
    public async Task RunAsync_AFailureAboutTheEndpoint_IsAnsweredByTheFallback(ChatGenerationFailure failure)
    {
        // Arrange
        var asked = new List<string>();
        using var logs = new RecordingLoggerProvider();
        var logger = logs.CreateLogger("chat");

        // Act
        var answer = await ChatModelFallThrough.RunAsync(
            PlanWithFallback(),
            logger,
            (model, _) =>
            {
                asked.Add(model.Endpoint.Alias);

                return model.Endpoint.Alias == "answering"
                    ? throw new ChatGenerationFailedException(model.Endpoint.Alias, failure)
                    : Task.FromResult("an answer the standby gave");
            },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("an answer the standby gave", answer);
        Assert.Equal(["answering", "standby"], asked);
    }

    /// <summary>A refusal is refused the same way by a second endpoint, and an empty answer is a call that succeeded — either one costs a second payment for the same outcome.</summary>
    [Theory]
    [InlineData(ChatGenerationFailure.RequestRefused)]
    [InlineData(ChatGenerationFailure.AnswerEmpty)]
    public async Task RunAsync_AFailureAboutTheRequest_IsRaisedWithoutAskingTheFallback(ChatGenerationFailure failure)
    {
        // Arrange
        var asked = new List<string>();
        using var logs = new RecordingLoggerProvider();
        var logger = logs.CreateLogger("chat");

        // Act
        var raised = await Assert.ThrowsAsync<ChatGenerationFailedException>(() => ChatModelFallThrough.RunAsync<string>(
            PlanWithFallback(),
            logger,
            (model, _) =>
            {
                asked.Add(model.Endpoint.Alias);

                throw new ChatGenerationFailedException(model.Endpoint.Alias, failure);
            },
            TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(failure, raised.Failure);
        Assert.Equal(["answering"], asked);
    }

    /// <summary>The fallback's own failure is what reaches the caller, because that is the model the call ended on.</summary>
    [Fact]
    public async Task RunAsync_NeitherModelAnswering_RaisesTheFallbacksOwnFailure()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var logger = logs.CreateLogger("chat");

        // Act
        var raised = await Assert.ThrowsAsync<ChatGenerationFailedException>(() => ChatModelFallThrough.RunAsync<string>(
            PlanWithFallback(),
            logger,
            (model, _) => throw new ChatGenerationFailedException(
                model.Endpoint.Alias,
                model.Endpoint.Alias == "answering"
                    ? ChatGenerationFailure.TransportFaulted
                    : ChatGenerationFailure.RateLimited),
            TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(ChatGenerationFailure.RateLimited, raised.Failure);
        Assert.Contains("standby", raised.Message, StringComparison.Ordinal);
    }

    /// <summary>A model with nothing behind it raises what it raised, whatever kind of failure that was.</summary>
    [Fact]
    public async Task RunAsync_AModelWithNoFallback_RaisesItsOwnFailure()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var logger = logs.CreateLogger("chat");

        // Act
        var raised = await Assert.ThrowsAsync<ChatGenerationFailedException>(() => ChatModelFallThrough.RunAsync<string>(
            ChatDeclarations.Plan(),
            logger,
            (model, _) => throw new ChatGenerationFailedException(
                model.Endpoint.Alias,
                ChatGenerationFailure.TransportFaulted),
            TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(ChatGenerationFailure.TransportFaulted, raised.Failure);
    }

    /// <summary>An operator reading the log has to see that the answer came from the second model rather than the declared one.</summary>
    [Fact]
    public async Task RunAsync_AFallThrough_RecordsBothModelsAndTheFailureThatCausedIt()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var logger = logs.CreateLogger("chat");

        // Act
        _ = await ChatModelFallThrough.RunAsync(
            PlanWithFallback(),
            logger,
            (model, _) => model.Endpoint.Alias == "answering"
                ? throw new ChatGenerationFailedException(model.Endpoint.Alias, ChatGenerationFailure.RateLimited)
                : Task.FromResult("an answer the standby gave"),
            TestContext.Current.CancellationToken);

        // Assert
        var record = Assert.Single(logs.Records);
        Assert.Contains(record.Properties, property => Equals(property.Value, "answering"));
        Assert.Contains(record.Properties, property => Equals(property.Value, "standby"));
        Assert.Contains(record.Properties, property => Equals(property.Value, ChatGenerationFailure.RateLimited));
    }

    [Fact]
    public void IsWorthAnotherModel_WithoutAFailure_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => ChatModelFallThrough.IsWorthAnotherModel(null!));
    }

    private static ChatGenerationPlan PlanWithFallback() => ChatDeclarations
        .Plan()
        .WithFallback(ChatDeclarations.Plan(ChatDeclarations.Endpoint("standby", routedModelName: "a-standby-model")));
}
