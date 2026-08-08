// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Text;
using MailFathom.AI.Chat;
using MailFathom.AI.ProviderAdapters;
using MailFathom.AI.Providers;
using MailFathom.AI.UnitTests.TestDoubles;
using MailFathom.Application.AiProviders;
using MailFathom.Application.Chat;
using MailFathom.Application.Resilience;
using MailFathom.Domain.Failures;
using MailFathom.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace MailFathom.AI.UnitTests.ProviderAdapters;

/// <summary>Covers the one boundary that speaks to a chat provider, over a real client and no network.</summary>
/// <remarks>
/// Every failure classification is proved here rather than against a provider, because the classification is what the
/// retry decision and the operator's next move both rest on and a test that spent money to establish it is a test
/// nobody would run. The scripted provider answers with the wire payloads a provider produces, so the client library
/// parses what it would really parse.
/// </remarks>
public sealed class ProviderChatModelClientTests
{
    private static readonly IReadOnlyList<ChatMessage> Conversation = [new(ChatRole.User, "what did they say")];

    [Fact]
    public async Task AnswerAsync_AProviderThatAnswered_ReturnsTheTextAndWhatItCost()
    {
        // Arrange
        using var provider = ScriptedProvider.Answering(Completion("they said yes", "stop", inputTokens: 11, outputTokens: 3));
        var client = provider.ClientOver(ChatDeclarations.Plan());

        // Act
        var answer = await client.AnswerAsync(Conversation, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("they said yes", answer.Text);
        Assert.Equal(ChatGenerationStop.Completed, answer.Stop);
        Assert.Equal(11, answer.Usage?.InputTokens);
        Assert.Equal(3, answer.Usage?.OutputTokens);
        provider.HealthRecorder.Received(1).RecordServed(AiProviderRole.Chat);
    }

    /// <summary>
    /// A truncated generation and one a content filter stopped are answers rather than failures, which is what
    /// guarantees neither is ever repeated as though it were a transport fault.
    /// </summary>
    [Theory]
    [InlineData("stop", ChatGenerationStop.Completed)]
    [InlineData("length", ChatGenerationStop.OutputLimitReached)]
    [InlineData("content_filter", ChatGenerationStop.ContentFiltered)]
    [InlineData("tool_calls", ChatGenerationStop.Unreported)]
    public async Task AnswerAsync_AFinishReason_IsReadIntoTheStopThisPortPublishes(
        string finishReason,
        ChatGenerationStop expected)
    {
        // Arrange
        using var provider = ScriptedProvider.Answering(Completion("a partial answer", finishReason));
        var client = provider.ClientOver(ChatDeclarations.Plan());

        // Act
        var answer = await client.AnswerAsync(Conversation, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(expected, answer.Stop);
        Assert.Equal(1, provider.RequestCount);
    }

    [Fact]
    public async Task AnswerAsync_AProviderThatReportedNoUsage_ReturnsTheAnswerWithoutIt()
    {
        // Arrange
        using var provider = ScriptedProvider.Answering(Completion("an answer", "stop", inputTokens: null, outputTokens: null));
        var client = provider.ClientOver(ChatDeclarations.Plan());

        // Act
        var answer = await client.AnswerAsync(Conversation, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("an answer", answer.Text);
        Assert.Null(answer.Usage);
    }

    /// <summary>An empty string reaching a caller reads as a model that had nothing to say rather than as a call that produced nothing.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AnswerAsync_AProviderThatProducedNoText_IsAnEmptyAnswerFailure(string content)
    {
        // Arrange
        using var provider = ScriptedProvider.Answering(Completion(content, "stop"));
        var client = provider.ClientOver(ChatDeclarations.Plan());

        // Act
        var failure = await Assert.ThrowsAsync<ChatGenerationFailedException>(() =>
            client.AnswerAsync(Conversation, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(ChatGenerationFailure.AnswerEmpty, failure.Failure);
        Assert.False(failure.IsWorthRepeating);
        Assert.Equal(MailFathomErrorCode.ChatAnswerEmpty, failure.ErrorCode);
    }

    /// <summary>
    /// An endpoint that took the request, authenticated it, ran the model, and came back with no text is a working
    /// endpoint. Reporting it as something to fix would send an operator after a deployment that has nothing wrong with
    /// it — which is the whole reason the health state is not simply "the last call failed".
    /// </summary>
    [Fact]
    public async Task AnswerAsync_AnEmptyAnswer_LeavesTheProviderReportedAsWorking()
    {
        // Arrange
        using var provider = ScriptedProvider.Answering(Completion(string.Empty, "content_filter"));
        var client = provider.ClientOver(ChatDeclarations.Plan());

        // Act
        await Assert.ThrowsAsync<ChatGenerationFailedException>(() =>
            client.AnswerAsync(Conversation, TestContext.Current.CancellationToken));

        // Assert
        provider.HealthRecorder.Received(1).RecordServed(AiProviderRole.Chat);
        provider.HealthRecorder.DidNotReceive().RecordMisconfigured(AiProviderRole.Chat);
        provider.HealthRecorder.DidNotReceive().RecordUnavailable(AiProviderRole.Chat);
    }

    /// <summary>The classification is what decides whether repeating buys anything, so each status is proved against it.</summary>
    [Theory]
    [InlineData(401, ChatGenerationFailure.CredentialRejected, false)]
    [InlineData(403, ChatGenerationFailure.CredentialRejected, false)]
    [InlineData(429, ChatGenerationFailure.RateLimited, true)]
    [InlineData(408, ChatGenerationFailure.RequestTimedOut, true)]
    [InlineData(500, ChatGenerationFailure.TransportFaulted, true)]
    [InlineData(400, ChatGenerationFailure.RequestRefused, false)]
    public async Task AnswerAsync_AProviderRefusal_IsClassifiedAndSaysWhetherRepeatingHelps(
        int status,
        ChatGenerationFailure expected,
        bool isWorthRepeating)
    {
        // Arrange
        using var provider = ScriptedProvider.Refusing((HttpStatusCode)status);
        var client = provider.ClientOver(ChatDeclarations.Plan());

        // Act
        var failure = await Assert.ThrowsAsync<ChatGenerationFailedException>(() =>
            client.AnswerAsync(Conversation, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(expected, failure.Failure);
        Assert.Equal(isWorthRepeating, failure.IsWorthRepeating);
    }

    /// <summary>
    /// The health state and the resilience pipeline read the same property, so a failure nobody can wait out is
    /// recorded as one an operator has to act on rather than as an outage.
    /// </summary>
    [Theory]
    [InlineData(429)]
    [InlineData(500)]
    public async Task AnswerAsync_AFailureWorthRepeating_RecordsTheProviderAsUnavailable(int status)
    {
        // Arrange
        using var provider = ScriptedProvider.Refusing((HttpStatusCode)status);
        var client = provider.ClientOver(ChatDeclarations.Plan());

        // Act
        await Assert.ThrowsAsync<ChatGenerationFailedException>(() =>
            client.AnswerAsync(Conversation, TestContext.Current.CancellationToken));

        // Assert
        provider.HealthRecorder.Received(1).RecordUnavailable(AiProviderRole.Chat);
        provider.HealthRecorder.DidNotReceive().RecordMisconfigured(AiProviderRole.Chat);
    }

    [Fact]
    public async Task AnswerAsync_ARefusedCredential_RecordsTheProviderAsMisconfigured()
    {
        // Arrange
        using var provider = ScriptedProvider.Refusing(HttpStatusCode.Unauthorized);
        var client = provider.ClientOver(ChatDeclarations.Plan());

        // Act
        await Assert.ThrowsAsync<ChatGenerationFailedException>(() =>
            client.AnswerAsync(Conversation, TestContext.Current.CancellationToken));

        // Assert
        provider.HealthRecorder.Received(1).RecordMisconfigured(AiProviderRole.Chat);
        provider.HealthRecorder.DidNotReceive().RecordUnavailable(AiProviderRole.Chat);
    }

    /// <summary>A transport fault never reaches a status, so it is classified from the failure alone.</summary>
    [Fact]
    public async Task AnswerAsync_AnEndpointThatCannotBeReached_IsATransportFault()
    {
        // Arrange
        using var provider = ScriptedProvider.Throwing(new HttpRequestException("no route to host"));
        var client = provider.ClientOver(ChatDeclarations.Plan());

        // Act
        var failure = await Assert.ThrowsAsync<ChatGenerationFailedException>(() =>
            client.AnswerAsync(Conversation, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(ChatGenerationFailure.TransportFaulted, failure.Failure);
    }

    /// <summary>
    /// The deadline is this deployment's, so a silent endpoint surfaces as a timeout that is classified and may be
    /// repeated rather than as a cancellation this system appears to have chosen.
    /// </summary>
    /// <remarks>
    /// The declared deadline is short because the adapter applies it through a linked cancellation token, which the
    /// platform's own timer drives — there is no clock to substitute here. The endpoint never answers at all, so the
    /// deadline always wins and the wait is the declared one rather than a race against it.
    /// </remarks>
    [Fact]
    public async Task AnswerAsync_AnEndpointThatOutlivesTheDeadline_IsARequestTimeout()
    {
        // Arrange
        using var provider = ScriptedProvider.Stalling();
        var client = provider.ClientOver(ChatDeclarations.Plan(requestTimeout: TimeSpan.FromMilliseconds(50)));

        // Act
        var failure = await Assert.ThrowsAsync<ChatGenerationFailedException>(() =>
            client.AnswerAsync(Conversation, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(ChatGenerationFailure.RequestTimedOut, failure.Failure);
    }

    /// <summary>A caller that cancelled is not a provider that failed, and the distinction has to survive the adapter.</summary>
    [Fact]
    public async Task AnswerAsync_ACallerThatCancelled_IsNotAProviderFailure()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        using var provider = ScriptedProvider.Throwing(new HttpRequestException("unreachable"));
        var client = provider.ClientOver(ChatDeclarations.Plan());

        await cancellation.CancelAsync();

        // Act
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.AnswerAsync(Conversation, cancellation.Token));

        // Assert
        provider.HealthRecorder.DidNotReceive().RecordUnavailable(AiProviderRole.Chat);
        provider.HealthRecorder.DidNotReceive().RecordMisconfigured(AiProviderRole.Chat);
    }

    /// <summary>
    /// An open circuit or a spent concurrency budget is exactly the condition a caller waits out, so it arrives as a
    /// transport fault rather than as a rejection nothing above this boundary could read.
    /// </summary>
    [Fact]
    public async Task AnswerAsync_APipelineThatDeclinedTheCall_IsATransportFault()
    {
        // Arrange
        using var provider = ScriptedProvider.Answering(Completion("never reached", "stop"));
        var client = provider.ClientOver(ChatDeclarations.Plan(), declineTheCall: true);

        // Act
        var failure = await Assert.ThrowsAsync<ChatGenerationFailedException>(() =>
            client.AnswerAsync(Conversation, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(ChatGenerationFailure.TransportFaulted, failure.Failure);
        Assert.Equal(0, provider.RequestCount);
    }

    [Fact]
    public async Task AnswerAsync_AConversationBeyondTheDeclaredBounds_IsRefusedBeforeTheProviderSeesIt()
    {
        // Arrange
        using var provider = ScriptedProvider.Answering(Completion("never reached", "stop"));
        var client = provider.ClientOver(ChatDeclarations.Plan(maximumMessagesPerRequest: 1));

        // Act
        await Assert.ThrowsAsync<ArgumentException>(() => client.AnswerAsync(
            [new(ChatRole.System, "answer briefly"), new(ChatRole.User, "what did they say")],
            TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(0, provider.RequestCount);
    }

    /// <summary>Every role this port publishes has to survive the translation, or a turn would reach the model as the wrong speaker.</summary>
    [Fact]
    public async Task AnswerAsync_AConversationOfEveryRole_SendsEachUnderItsOwnRole()
    {
        // Arrange
        using var provider = ScriptedProvider.Answering(Completion("an answer", "stop"));
        var client = provider.ClientOver(ChatDeclarations.Plan());

        // Act
        await client.AnswerAsync(
            [
                new(ChatRole.System, "answer briefly"),
                new(ChatRole.User, "what did they say"),
                new(ChatRole.Assistant, "they said yes"),
                new(ChatRole.User, "and then"),
            ],
            TestContext.Current.CancellationToken);

        // Assert
        var sent = provider.LastRequestBody;

        Assert.Contains("\"role\":\"system\"", sent, StringComparison.Ordinal);
        Assert.Contains("\"role\":\"user\"", sent, StringComparison.Ordinal);
        Assert.Contains("\"role\":\"assistant\"", sent, StringComparison.Ordinal);
    }

    /// <summary>The parameters are the deployment's, so a request carries the declared budget rather than the library's default.</summary>
    [Fact]
    public async Task AnswerAsync_ADeclaredOutputBudget_ReachesTheRequest()
    {
        // Arrange
        using var provider = ScriptedProvider.Answering(Completion("an answer", "stop"));
        var client = provider.ClientOver(ChatDeclarations.Plan(maximumOutputTokens: 321, temperature: 0.25f));

        // Act
        await client.AnswerAsync(Conversation, TestContext.Current.CancellationToken);

        // Assert
        var sent = provider.LastRequestBody;

        Assert.Contains("321", sent, StringComparison.Ordinal);
        Assert.Contains("0.25", sent, StringComparison.Ordinal);
    }

    /// <summary>Builds the chat-completion payload a provider answers with.</summary>
    /// <remarks>
    /// A <see langword="null" /> <paramref name="finishReason" /> leaves the member out of the payload, because the
    /// client library refuses a JSON null outright. It is not asserted on: the library supplies its own default for an
    /// absent member, so an omitted reason is indistinguishable from a stated one by the time the adapter sees it.
    /// </remarks>
    private static string Completion(
        string content,
        string? finishReason,
        long? inputTokens = 1,
        long? outputTokens = 1)
    {
        var finish = finishReason is null ? string.Empty : $",\"finish_reason\":\"{finishReason}\"";
        var usage = inputTokens is null || outputTokens is null
            ? string.Empty
            : $",\"usage\":{{\"prompt_tokens\":{inputTokens},\"completion_tokens\":{outputTokens},"
                + $"\"total_tokens\":{inputTokens + outputTokens}}}";

        return "{\"id\":\"chatcmpl-1\",\"object\":\"chat.completion\",\"created\":1,\"model\":\"a-chat-model\","
            + "\"choices\":[{\"index\":0,\"message\":{\"role\":\"assistant\",\"content\":\""
            + content
            + "\"}"
            + finish
            + "}]"
            + usage
            + "}";
    }

    /// <summary>Stands in for whatever the resilience boundary raises when a configured limit stopped an operation.</summary>
    /// <remarks>
    /// A type of its own rather than the real one, and that is the point being proved rather than a convenience: the
    /// adapter recognizes this failure by its stable error code, because the exception that carries it belongs to
    /// another adapter boundary this one may not reference.
    /// </remarks>
    [SuppressMessage("Design", "CA1064:Exceptions should be public", Justification = "A test double for one behavior of one test class, which nothing outside it raises or catches.")]
    private sealed class PipelineDeclinedTheOperation()
        : MailFathomException("An outbound resilience limit stopped the operation.")
    {
        public override MailFathomErrorCode ErrorCode => MailFathomErrorCode.OutboundDependencyUnavailable;
    }

    /// <summary>A provider that answers from a script, so the adapter is exercised over a real client and no network.</summary>
    private sealed class ScriptedProvider : IDisposable
    {
        private readonly List<string> requestBodies = [];
        private readonly FakeHttpMessageHandler handler;

        private Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> answer = (_, _) =>
            throw new InvalidOperationException("The script names no answer.");

        private ScriptedProvider() =>
            this.handler = new FakeHttpMessageHandler(async (request, cancellationToken) =>
            {
                this.requestBodies.Add(request.Content is null
                    ? string.Empty
                    : await request.Content.ReadAsStringAsync(cancellationToken));

                return await this.answer(request, cancellationToken);
            });

        public int RequestCount => this.requestBodies.Count;

        public string LastRequestBody => this.requestBodies[^1];

        /// <summary>The health state every call this provider serves reports into, so a test can read what the run established.</summary>
        public IAiProviderHealthRecorder HealthRecorder { get; } = Substitute.For<IAiProviderHealthRecorder>();

        public static ScriptedProvider Answering(string payload)
        {
            var provider = new ScriptedProvider();
            provider.answer = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json"),
            });

            return provider;
        }

        public static ScriptedProvider Refusing(HttpStatusCode status)
        {
            var provider = new ScriptedProvider();
            provider.answer = (_, _) => Task.FromResult(new HttpResponseMessage(status)
            {
                // A provider quotes the request in its error body, so the adapter must classify from the status alone.
                Content = new StringContent(
                    "{\"error\":{\"message\":\"what did they say\"}}",
                    Encoding.UTF8,
                    "application/json"),
            });

            return provider;
        }

        public static ScriptedProvider Throwing(Exception failure)
        {
            var provider = new ScriptedProvider();
            provider.answer = (_, _) => throw failure;

            return provider;
        }

        /// <summary>An endpoint that never answers, so the adapter's own deadline is what ends the attempt.</summary>
        public static ScriptedProvider Stalling()
        {
            var provider = new ScriptedProvider();
            provider.answer = async (_, cancellationToken) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);

                throw new InvalidOperationException("The stall was never meant to end.");
            };

            return provider;
        }

        public ProviderChatModelClient ClientOver(ChatGenerationPlan plan, bool declineTheCall = false)
        {
            var transportFactory = Substitute.For<IHttpClientFactory>();
            transportFactory
                .CreateClient(Arg.Any<string>())
                .Returns(_ => new HttpClient(this.handler, disposeHandler: false));

            var credentialSource = Substitute.For<IProviderEndpointCredentialSource>();
            credentialSource
                .ResolveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(_ => Task.FromResult(
                    ProviderEndpointCredential.FromApiKey("a-configured-key", resolvedMaterial: null)));

            var operationRunner = Substitute.For<IOutboundOperationRunner>();
            operationRunner
                .RunAsync(
                    Arg.Any<OutboundDependency>(),
                    Arg.Any<string>(),
                    Arg.Any<Func<CancellationToken, Task<Microsoft.Extensions.AI.ChatResponse>>>(),
                    Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    if (declineTheCall)
                    {
                        throw new PipelineDeclinedTheOperation();
                    }

                    // The substitute cannot know the argument is present, and it always is: this configuration matches
                    // the only overload the adapter calls.
                    var operation = call.Arg<Func<CancellationToken, Task<Microsoft.Extensions.AI.ChatResponse>>>()!;

                    return operation(call.Arg<CancellationToken>());
                });

            return new ProviderChatModelClient(
                plan,
                credentialSource,
                new OpenAiCompatibleClientFactory(),
                transportFactory,
                operationRunner,
                this.HealthRecorder,
                NullLogger<ProviderChatModelClient>.Instance);
        }

        public void Dispose() => this.handler.Dispose();
    }
}
