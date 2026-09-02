// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
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
using MailFathom.Application.SensitiveContent.Detection;
using MailFathom.Application.SensitiveContent.Egress;
using MailFathom.Domain.Failures;
using MailFathom.TestSupport;
using Microsoft.Extensions.Logging;
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
    /// <summary>The literal the scanner in the guarded-egress tests reports, standing in for a credential in mail.</summary>
    private const string Marker = "AKIAEXAMPLEKEY";

    private static readonly IReadOnlyList<ChatMessage> Conversation = [new(ChatRole.User, "what did they say")];

    /// <summary>The distinguishing text of each turn the ordering test sends, in the order it sends them.</summary>
    private static readonly string[] TurnsInOrder =
        ["answer briefly", "what did they say", "they said yes", "and then"];

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

    /// <summary>
    /// The classification is what decides whether repeating buys anything, and the error code beside it is the stable
    /// operator-facing identity ADR 0003 publishes — a log search, an alert, and a support conversation all name it, so
    /// a wrong entry in the mapping is asserted here rather than discovered from a runbook that stopped matching.
    /// </summary>
    [Theory]
    [InlineData(401, ChatGenerationFailure.CredentialRejected, false, 71001)]
    [InlineData(403, ChatGenerationFailure.CredentialRejected, false, 71001)]
    [InlineData(429, ChatGenerationFailure.RateLimited, true, 72001)]
    [InlineData(408, ChatGenerationFailure.RequestTimedOut, true, 72001)]
    [InlineData(500, ChatGenerationFailure.TransportFaulted, true, 72001)]
    [InlineData(400, ChatGenerationFailure.RequestRefused, false, 72001)]
    public async Task AnswerAsync_AProviderRefusal_IsClassifiedAndSaysWhetherRepeatingHelps(
        int status,
        ChatGenerationFailure expected,
        bool isWorthRepeating,
        int expectedErrorCode)
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
        Assert.Equal(expectedErrorCode, failure.ErrorCode.Value);
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

    /// <summary>A conversation reaching this port is already built, so every turn of it is scanned before any is sent.</summary>
    [Fact]
    public async Task AnswerAsync_ASwitchedOnScanner_RedactsEveryTurnBeforeTheRequestIsSent()
    {
        // Arrange
        using var egress = ScanningSensitiveContentEgress.Finding(Marker, TimeProvider.System);
        using var actingFor = egress.ActingForOwner();
        using var provider = ScriptedProvider.Answering(Completion("an answer", "stop"));
        var client = provider.ClientOver(ChatDeclarations.Plan(), egressGuard: egress.Guard);

        // Act
        await client.AnswerAsync(
            [
                new(ChatRole.System, "answer briefly"),
                new(ChatRole.User, $"is {Marker} still valid"),
            ],
            TestContext.Current.CancellationToken);

        // Assert
        var sent = provider.LastRequestBody;

        Assert.DoesNotContain(Marker, sent, StringComparison.Ordinal);
        Assert.Contains("[redacted:CloudKey]", sent, StringComparison.Ordinal);
    }

    /// <summary>Sending a prompt a scanner could not read would be the leak the switch was turned on to prevent.</summary>
    [Fact]
    public async Task AnswerAsync_ADetectorThatCannotAnswer_RefusesTheCallWithoutReachingTheProvider()
    {
        // Arrange
        using var egress = ScanningSensitiveContentEgress.Unavailable(TimeProvider.System);
        using var actingFor = egress.ActingForOwner();
        using var provider = ScriptedProvider.Answering(Completion("never reached", "stop"));
        var client = provider.ClientOver(ChatDeclarations.Plan(), egressGuard: egress.Guard);

        // Act
        await Assert.ThrowsAsync<SensitiveContentScannerUnavailableException>(() =>
            client.AnswerAsync(Conversation, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(0, provider.RequestCount);
    }

    /// <summary>An opt-in nobody took must not appear on this path at all.</summary>
    [Fact]
    public async Task AnswerAsync_ADeploymentThatScansNothing_SendsEveryTurnAsItWasComposed()
    {
        // Arrange
        using var provider = ScriptedProvider.Answering(Completion("an answer", "stop"));
        var client = provider.ClientOver(ChatDeclarations.Plan());

        // Act
        await client.AnswerAsync(
            [new(ChatRole.User, $"is {Marker} still valid")],
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(Marker, provider.LastRequestBody, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every role this port publishes has to survive the translation, and the turns have to arrive in the order they
    /// were given. A conversation the model receives reordered is a different conversation, and nothing above this
    /// boundary could tell — so the order is asserted by position rather than by each role merely being present.
    /// </summary>
    [Fact]
    public async Task AnswerAsync_AConversationOfEveryRole_SendsEachUnderItsOwnRoleInOrder()
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
        var positions = TurnsInOrder
            .Select(turn => sent.IndexOf(turn, StringComparison.Ordinal))
            .ToArray();

        Assert.DoesNotContain(-1, positions);
        Assert.Equal(positions.Order(), positions);

        Assert.Contains("\"role\":\"system\"", sent, StringComparison.Ordinal);
        Assert.Contains("\"role\":\"user\"", sent, StringComparison.Ordinal);
        Assert.Contains("\"role\":\"assistant\"", sent, StringComparison.Ordinal);
    }

    /// <summary>
    /// The invariant this whole boundary rests on: a prompt is somebody's question and the passages of their mail, an
    /// answer is written from both, and a provider's own error text quotes the request that produced it. None of it may
    /// reach a log record — neither the formatted message nor the named values a structured sink would export.
    /// </summary>
    /// <remarks>
    /// The exception is deliberately excluded from the search. The adapter attaches the provider's failure as an inner
    /// exception, which is what preserves the cause, and a log sink that renders an exception is making its own choice
    /// about what to write; what this asserts is that MailFathom's own record carries none of it.
    /// </remarks>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AnswerAsync_WhateverTheCallDid_LogsNoPromptAnswerOrProviderText(bool providerAnswers)
    {
        // Arrange
        const string prompt = "what did the auditors say about the Q3 shortfall";
        const string answer = "they flagged an unreconciled ledger";

        using var recordingLoggers = new RecordingLoggerProvider();
        using var provider = providerAnswers
            ? ScriptedProvider.Answering(Completion(answer, "stop"))
            : ScriptedProvider.Refusing(HttpStatusCode.BadRequest);

        var client = provider.ClientOver(
            ChatDeclarations.Plan(),
            logger: recordingLoggers.CreateLogger(typeof(ProviderChatModelClient).FullName!));

        // Act
        await Record.ExceptionAsync(() => client.AnswerAsync(
            [new(ChatRole.User, prompt)],
            TestContext.Current.CancellationToken));

        // Assert
        Assert.NotEmpty(recordingLoggers.Records);

        var written = recordingLoggers.Records
            .SelectMany(record => record.Properties
                .Select(property => property.Value?.ToString() ?? string.Empty)
                .Append(record.Message))
            .ToArray();

        Assert.DoesNotContain(written, entry => entry.Contains(prompt, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(written, entry => entry.Contains(answer, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(written, entry => entry.Contains("a-configured-key", StringComparison.OrdinalIgnoreCase));

        // The endpoint alias is the operator's own name for the endpoint and is what a record may carry instead.
        Assert.Contains(written, entry => entry.Contains("answering", StringComparison.Ordinal));
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

    /// <summary>
    /// The parameter this whole issue exists for: a model that refuses function tools beside an unstated effort has to
    /// be reachable, and the wire name of each effort is what the provider reads. Asserted on the request rather than on
    /// the plan, because a value that reached the plan and not the request would leave the refusal exactly where it was.
    /// </summary>
    /// <remarks>
    /// The last two cases are the point of the parameter being a word rather than a member of a set: <c>xhigh</c>
    /// arrived after the levels beneath it, and a level released after this build has to reach the provider without one.
    /// </remarks>
    [Theory]
    [InlineData("none")]
    [InlineData("minimal")]
    [InlineData("low")]
    [InlineData("high")]
    [InlineData("xhigh")]
    [InlineData("a-level-released-later")]
    public async Task AnswerAsync_ADeclaredReasoningEffort_ReachesTheRequestAsWritten(string effort)
    {
        // Arrange
        using var provider = ScriptedProvider.Answering(Completion("an answer", "stop"));
        var client = provider.ClientOver(ChatDeclarations.Plan(reasoningEffort: effort));

        // Act
        await client.AnswerAsync(Conversation, TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains($"\"reasoning_effort\":\"{effort}\"", provider.LastRequestBody, StringComparison.Ordinal);
    }

    /// <summary>
    /// A section that writes no effort sends none, because a model that does not reason rejects the parameter outright
    /// and a literal default would turn every call such a deployment makes into a rejected request.
    /// </summary>
    [Fact]
    public async Task AnswerAsync_WithoutADeclaredReasoningEffort_SendsNoReasoningParameter()
    {
        // Arrange
        using var provider = ScriptedProvider.Answering(Completion("an answer", "stop"));
        var client = provider.ClientOver(ChatDeclarations.Plan());

        // Act
        await client.AnswerAsync(Conversation, TestContext.Current.CancellationToken);

        // Assert
        Assert.DoesNotContain("reasoning", provider.LastRequestBody, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The declared API decides the path, and that is the whole observable difference: the same endpoint, the same
    /// credential, and the same transport carry both, so what a test can see is where the request went.
    /// </summary>
    [Theory]
    [InlineData(ChatProviderApi.ChatCompletions, "/v1/chat/completions")]
    [InlineData(ChatProviderApi.Responses, "/v1/responses")]
    public async Task AnswerAsync_ADeclaredApi_SendsToThatApisPath(ChatProviderApi api, string path)
    {
        // Arrange
        using var provider = ScriptedProvider.Answering(
            api is ChatProviderApi.Responses ? Response("an answer") : Completion("an answer", "stop"));

        var client = provider.ClientOver(ChatDeclarations.Plan(ChatDeclarations.Endpoint(api: api)));

        // Act
        var answer = await client.AnswerAsync(Conversation, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(path, provider.LastRequestPath);
        Assert.Equal("an answer", answer.Text);
    }

    /// <summary>The responses API states the effort in a block of its own, so the mapping has to survive the other surface too.</summary>
    [Theory]
    [InlineData("low")]
    [InlineData("a-level-released-later")]
    public async Task AnswerAsync_AReasoningEffortOverTheResponsesApi_ReachesTheRequest(string effort)
    {
        // Arrange
        using var provider = ScriptedProvider.Answering(Response("an answer"));
        var client = provider.ClientOver(ChatDeclarations.Plan(
            ChatDeclarations.Endpoint(api: ChatProviderApi.Responses),
            reasoningEffort: effort));

        // Act
        await client.AnswerAsync(Conversation, TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains($"\"reasoning\":{{\"effort\":\"{effort}\"}}", provider.LastRequestBody, StringComparison.Ordinal);
    }

    /// <summary>
    /// The responses API keeps what it is sent unless the request says otherwise, and what one request here carries is
    /// the question together with the mail passages retrieval selected for it. Storing that is the provider's default
    /// rather than this deployment's decision, so the refusal is stated on every call the API conducts — which is why
    /// it holds over a declaration that wrote no reasoning effort as firmly as over one that did.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("low")]
    public async Task AnswerAsync_OverTheResponsesApi_LeavesNoStoredCopyAtTheProvider(string? effort)
    {
        // Arrange
        using var provider = ScriptedProvider.Answering(Response("an answer"));
        var client = provider.ClientOver(ChatDeclarations.Plan(
            ChatDeclarations.Endpoint(api: ChatProviderApi.Responses),
            reasoningEffort: effort));

        // Act
        await client.AnswerAsync(Conversation, TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains("\"store\":false", provider.LastRequestBody, StringComparison.Ordinal);
    }

    /// <summary>
    /// Storing nothing at the provider is what makes a run stateless, and a stateless run carries its own reasoning:
    /// the model returns it encrypted and reads it back on the turn after, but only where the request asked to have it
    /// included. Without that a tool loop starts every turn without what it worked out in the one before.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("low")]
    public async Task AnswerAsync_OverTheResponsesApi_AsksForTheReasoningItWillHandBack(string? effort)
    {
        // Arrange
        using var provider = ScriptedProvider.Answering(Response("an answer"));
        var client = provider.ClientOver(ChatDeclarations.Plan(
            ChatDeclarations.Endpoint(api: ChatProviderApi.Responses),
            reasoningEffort: effort));

        // Act
        await client.AnswerAsync(Conversation, TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(
            "\"include\":[\"reasoning.encrypted_content\"]",
            provider.LastRequestBody,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The chat completions API stores nothing unless it is asked to and has no inclusion list at all, so neither
    /// member belongs on a request it conducts. Sending one would be this deployment restating a default it agrees
    /// with, on the one surface where a member nobody asked for is what a model rejects the whole request over.
    /// </summary>
    [Fact]
    public async Task AnswerAsync_OverTheChatCompletionsApi_SendsNeitherResponsesOnlyMember()
    {
        // Arrange
        using var provider = ScriptedProvider.Answering(Completion("an answer", "stop"));
        var client = provider.ClientOver(ChatDeclarations.Plan(reasoningEffort: "low"));

        // Act
        await client.AnswerAsync(Conversation, TestContext.Current.CancellationToken);

        // Assert
        Assert.DoesNotContain("\"store\"", provider.LastRequestBody, StringComparison.Ordinal);
        Assert.DoesNotContain("\"include\"", provider.LastRequestBody, StringComparison.Ordinal);
    }

    /// <summary>
    /// The responses surface names no reason for an answer that simply finished, so this deployment reports the honest
    /// reading rather than claiming the model completed. A truncation and a filtered generation still arrive named,
    /// which is what keeps either from being repeated as though it were a transport fault.
    /// </summary>
    [Theory]
    [InlineData(null, ChatGenerationStop.Unreported)]
    [InlineData("max_output_tokens", ChatGenerationStop.OutputLimitReached)]
    [InlineData("content_filter", ChatGenerationStop.ContentFiltered)]
    public async Task AnswerAsync_AResponsesApiOutcome_IsReadIntoTheStopThisPortPublishes(
        string? incompleteReason,
        ChatGenerationStop expected)
    {
        // Arrange
        using var provider = ScriptedProvider.Answering(Response("a partial answer", incompleteReason));
        var client = provider.ClientOver(
            ChatDeclarations.Plan(ChatDeclarations.Endpoint(api: ChatProviderApi.Responses)));

        // Act
        var answer = await client.AnswerAsync(Conversation, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(expected, answer.Stop);
    }

    /// <summary>Builds the responses payload a provider answers with.</summary>
    /// <remarks>
    /// The surface reports an outcome rather than a finish reason: a response that completed says so in its status and
    /// names nothing else, and one that stopped early names why under <c>incomplete_details</c>.
    /// </remarks>
    private static string Response(string content, string? incompleteReason = null)
    {
        var outcome = incompleteReason is null
            ? "\"status\":\"completed\""
            : $"\"status\":\"incomplete\",\"incomplete_details\":{{\"reason\":\"{incompleteReason}\"}}";

        return "{\"id\":\"resp-1\",\"object\":\"response\",\"created_at\":1,\"model\":\"a-chat-model\","
            + outcome
            + ",\"output\":[{\"type\":\"message\",\"id\":\"msg-1\",\"status\":\"completed\",\"role\":\"assistant\","
            + "\"content\":[{\"type\":\"output_text\",\"text\":\""
            + content
            + "\",\"annotations\":[]}]}],"
            + "\"usage\":{\"input_tokens\":11,\"output_tokens\":3,\"total_tokens\":14}}";
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

    /// <summary>Gives an untyped logger the category-typed shape a constructor asks for, writing through to it unchanged.</summary>
    /// <remarks>A shim rather than a second recorder, so what a test reads is the shared recording provider's records.</remarks>
    private sealed class TypedLogger<TCategory>(ILogger inner) : ILogger<TCategory>
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => inner.BeginScope(state);

        public bool IsEnabled(LogLevel logLevel) => inner.IsEnabled(logLevel);

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            inner.Log(logLevel, eventId, state, exception, formatter);
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
        private readonly List<string> requestPaths = [];
        private readonly FakeHttpMessageHandler handler;

        private Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> answer = (_, _) =>
            throw new InvalidOperationException("The script names no answer.");

        private ScriptedProvider() =>
            this.handler = new FakeHttpMessageHandler(async (request, cancellationToken) =>
            {
                this.requestBodies.Add(request.Content is null
                    ? string.Empty
                    : await request.Content.ReadAsStringAsync(cancellationToken));
                this.requestPaths.Add(request.RequestUri?.AbsolutePath ?? string.Empty);

                return await this.answer(request, cancellationToken);
            });

        public int RequestCount => this.requestBodies.Count;

        public string LastRequestBody => this.requestBodies[^1];

        /// <summary>The path the last request went to, which is what tells the two provider APIs apart on the wire.</summary>
        public string LastRequestPath => this.requestPaths[^1];

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

        public ProviderChatModelClient ClientOver(
            ChatGenerationPlan plan,
            bool declineTheCall = false,
            ILogger? logger = null,
            SensitiveContentEgressGuard? egressGuard = null)
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
                egressGuard ?? SensitiveContentEgressGuards.Inactive(),
                // Wrapped rather than required, because only the no-log test reads what was written and every other
                // test would otherwise carry a recorder it never asserts on.
                logger is null
                    ? NullLogger<ProviderChatModelClient>.Instance
                    : new TypedLogger<ProviderChatModelClient>(logger));
        }

        public void Dispose() => this.handler.Dispose();
    }
}
