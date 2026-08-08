// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using System.Net;
using MailFathom.AI.ProviderAdapters;
using MailFathom.AI.UnitTests.TestDoubles;
using MailFathom.Application.AiProviders;
using MailFathom.Application.Chat;
using MailFathom.Application.Resilience;
using MailFathom.Domain.Failures;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace MailFathom.AI.UnitTests.ProviderAdapters;

/// <summary>Covers the bounds every chat call of an agent run passes through.</summary>
/// <remarks>
/// The decorator is what a run has instead of the single-request adapter's per-call arrangement, so the same four
/// properties are proved here: one call is deadlined, a provider failure is classified rather than passed on, the health
/// state hears what each call established, and a pipeline that declined to call at all is a transport fault.
/// </remarks>
public sealed class ResilientChatClientTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromMilliseconds(200);

    private static readonly Microsoft.Extensions.AI.ChatMessage[] Conversation =
        [new(Microsoft.Extensions.AI.ChatRole.User, "what did they say")];

    [Fact]
    public async Task GetResponseAsync_AProviderThatAnswered_ReturnsTheAnswerAndReportsTheProviderWorking()
    {
        // Arrange
        var healthRecorder = Substitute.For<IAiProviderHealthRecorder>();
        using var inner = ScriptedChatClient.Answering("they said yes");
        using var client = ClientOver(inner, healthRecorder);

        // Act
        var response = await client.GetResponseAsync(
            Conversation,
            options: null,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("they said yes", response.Text);
        healthRecorder.Received(1).RecordServed(AiProviderRole.Chat);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, ChatGenerationFailure.CredentialRejected, false)]
    [InlineData(HttpStatusCode.TooManyRequests, ChatGenerationFailure.RateLimited, true)]
    [InlineData(HttpStatusCode.BadRequest, ChatGenerationFailure.RequestRefused, false)]
    [InlineData(HttpStatusCode.BadGateway, ChatGenerationFailure.TransportFaulted, true)]
    public async Task GetResponseAsync_AProviderThatRefused_IsClassifiedAndReportedAtThatGranularity(
        HttpStatusCode status,
        ChatGenerationFailure expected,
        bool isWorthRepeating)
    {
        // Arrange
        var healthRecorder = Substitute.For<IAiProviderHealthRecorder>();
        using var inner = FailingChatClient.Throwing(
            new HttpRequestException($"The endpoint refused with {(int)status}.", inner: null, status));
        using var client = ClientOver(inner, healthRecorder);

        // Act
        var failure = await Assert.ThrowsAsync<ChatGenerationFailedException>(() =>
            client.GetResponseAsync(Conversation, options: null, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(expected, failure.Failure);

        if (isWorthRepeating)
        {
            healthRecorder.Received(1).RecordUnavailable(AiProviderRole.Chat);
        }
        else
        {
            healthRecorder.Received(1).RecordMisconfigured(AiProviderRole.Chat);
        }
    }

    /// <summary>The deadline is this deployment's, so a slow endpoint surfaces as its timeout rather than as a cancellation.</summary>
    [Fact]
    public async Task GetResponseAsync_AnEndpointThatNeverAnswers_EndsAsThisDeploymentsTimeout()
    {
        // Arrange
        var healthRecorder = Substitute.For<IAiProviderHealthRecorder>();
        using var inner = FailingChatClient.Stalling();
        using var client = ClientOver(inner, healthRecorder);

        // Act
        var failure = await Assert.ThrowsAsync<ChatGenerationFailedException>(() =>
            client.GetResponseAsync(Conversation, options: null, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(ChatGenerationFailure.RequestTimedOut, failure.Failure);
        healthRecorder.Received(1).RecordUnavailable(AiProviderRole.Chat);
    }

    /// <summary>A caller who stopped waiting says nothing about the provider, so it must not be reported against it.</summary>
    [Fact]
    public async Task GetResponseAsync_ACallerThatCancelled_IsNotReportedAsAProviderFailure()
    {
        // Arrange
        var healthRecorder = Substitute.For<IAiProviderHealthRecorder>();
        using var inner = FailingChatClient.Stalling();
        using var client = ClientOver(inner, healthRecorder);
        using var caller = new CancellationTokenSource();

        await caller.CancelAsync();

        // Act
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.GetResponseAsync(Conversation, options: null, caller.Token));

        // Assert
        healthRecorder.DidNotReceive().RecordUnavailable(AiProviderRole.Chat);
        healthRecorder.DidNotReceive().RecordMisconfigured(AiProviderRole.Chat);
        healthRecorder.DidNotReceive().RecordServed(AiProviderRole.Chat);
    }

    /// <summary>A pipeline that declined to call the endpoint is recognized by its stable error code, not by its type.</summary>
    [Fact]
    public async Task GetResponseAsync_APipelineThatDeclinedTheCall_IsATransportFault()
    {
        // Arrange
        var healthRecorder = Substitute.For<IAiProviderHealthRecorder>();
        using var inner = ScriptedChatClient.Answering("never reached");
        using var client = ClientOver(inner, healthRecorder, declineTheCall: true);

        // Act
        var failure = await Assert.ThrowsAsync<ChatGenerationFailedException>(() =>
            client.GetResponseAsync(Conversation, options: null, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(ChatGenerationFailure.TransportFaulted, failure.Failure);
        Assert.Equal(MailFathomErrorCode.ChatProviderUnavailable, failure.ErrorCode);
    }

    /// <summary>An unbounded path to the provider must not exist for a later caller to find.</summary>
    [Fact]
    public void GetStreamingResponseAsync_AnyCall_IsRefusedRatherThanSentUnbounded()
    {
        // Arrange
        using var inner = ScriptedChatClient.Answering("never reached");
        using var client = ClientOver(inner, Substitute.For<IAiProviderHealthRecorder>());

        // Act, Assert
        Assert.Throws<NotSupportedException>(() =>
            client.GetStreamingResponseAsync(Conversation, options: null, TestContext.Current.CancellationToken));
    }

    private static ResilientChatClient ClientOver(
        Microsoft.Extensions.AI.IChatClient inner,
        IAiProviderHealthRecorder healthRecorder,
        bool declineTheCall = false)
    {
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

                // The substitute cannot know the argument is present, and it always is: this configuration matches the
                // only overload the decorator calls.
                var operation = call.Arg<Func<CancellationToken, Task<Microsoft.Extensions.AI.ChatResponse>>>()!;

                return operation(call.Arg<CancellationToken>());
            });

        return new ResilientChatClient(
            inner,
            ChatDeclarations.Endpoint(),
            Deadline,
            operationRunner,
            healthRecorder,
            NullLogger.Instance);
    }

    /// <summary>Stands in for whatever the resilience boundary raises when a configured limit stopped an operation.</summary>
    [SuppressMessage("Design", "CA1064:Exceptions should be public", Justification = "A test double for one behavior of one test class, which nothing outside it raises or catches.")]
    private sealed class PipelineDeclinedTheOperation()
        : MailFathomException("An outbound resilience limit stopped the operation.")
    {
        public override MailFathomErrorCode ErrorCode => MailFathomErrorCode.OutboundDependencyUnavailable;
    }

    /// <summary>A provider client that fails the way a test names, so the decorator's own reading of it is what is proved.</summary>
    private sealed class FailingChatClient : Microsoft.Extensions.AI.IChatClient
    {
        private readonly Func<CancellationToken, Task<Microsoft.Extensions.AI.ChatResponse>> answer;

        private FailingChatClient(Func<CancellationToken, Task<Microsoft.Extensions.AI.ChatResponse>> answer) =>
            this.answer = answer;

        public static FailingChatClient Throwing(Exception failure) => new(_ => throw failure);

        /// <summary>An endpoint that never answers, so the decorator's own deadline is what ends the call.</summary>
        public static FailingChatClient Stalling() => new(async cancellationToken =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);

            throw new InvalidOperationException("The stall was never meant to end.");
        });

        public Task<Microsoft.Extensions.AI.ChatResponse> GetResponseAsync(
            IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages,
            Microsoft.Extensions.AI.ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            this.answer(cancellationToken);

        public IAsyncEnumerable<Microsoft.Extensions.AI.ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages,
            Microsoft.Extensions.AI.ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("This client answers whole responses only.");

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
            // Nothing is held.
        }
    }
}
