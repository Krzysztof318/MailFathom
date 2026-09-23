// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace MailFathom.Evaluations.Providers;

/// <summary>Proves a provider call is asked again only when the provider failed transiently, and only up to its bound.</summary>
public sealed class TransientProviderRetryChatClientTests
{
    private const string Answer = "The export failed on build 4.8.2.";

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task GetResponseAsync_AProviderThatWasBusy_AsksAgainAndReturnsTheLaterAnswer(HttpStatusCode status)
    {
        // Arrange
        var clock = new FakeTimeProvider();
        using var provider = new ScriptedProvider(clock, new HttpRequestException("busy", inner: null, status));
        using var client = new TransientProviderRetryChatClient(provider, clock);

        // Act
        var response = await AdvanceUntilCompletedAsync(clock, AskAsync(client, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(Answer, response.Text);
        Assert.Equal(2, provider.Calls);
    }

    [Fact]
    public async Task GetResponseAsync_ARequestAbandonedAtTheTransportTimeout_AsksAgainAndReturnsTheLaterAnswer()
    {
        // Arrange
        var clock = new FakeTimeProvider();
        using var provider = new ScriptedProvider(clock, new TaskCanceledException("timed out", new TimeoutException()));
        using var client = new TransientProviderRetryChatClient(provider, clock);

        // Act
        var response = await AdvanceUntilCompletedAsync(clock, AskAsync(client, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(Answer, response.Text);
        Assert.Equal(2, provider.Calls);
    }

    [Fact]
    public async Task GetResponseAsync_AConnectionThatFailedBeforeAnyAnswer_AsksAgainAndReturnsTheLaterAnswer()
    {
        // Arrange
        var clock = new FakeTimeProvider();
        using var provider = new ScriptedProvider(clock, new HttpRequestException("connection reset"));
        using var client = new TransientProviderRetryChatClient(provider, clock);

        // Act
        var response = await AdvanceUntilCompletedAsync(clock, AskAsync(client, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(Answer, response.Text);
        Assert.Equal(2, provider.Calls);
    }

    [Fact]
    public async Task GetResponseAsync_ARateLimitedCall_WaitsBeforeAskingAgain()
    {
        // Arrange
        var clock = new FakeTimeProvider();
        using var provider = new ScriptedProvider(clock, new HttpRequestException("throttled", inner: null, HttpStatusCode.TooManyRequests));
        using var client = new TransientProviderRetryChatClient(provider, clock);

        // Act
        await AdvanceUntilCompletedAsync(clock, AskAsync(client, TestContext.Current.CancellationToken));

        // Assert
        Assert.True(provider.AskedAt[1] > provider.AskedAt[0]);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task GetResponseAsync_ARefusedRequestOrRejectedCredential_FailsWithoutAskingAgain(HttpStatusCode status)
    {
        // Arrange
        var clock = new FakeTimeProvider();
        using var provider = new ScriptedProvider(clock, new HttpRequestException("refused", inner: null, status));
        using var client = new TransientProviderRetryChatClient(provider, clock);

        // Act
        var failure = await Assert.ThrowsAsync<HttpRequestException>(() => AskAsync(client, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(status, failure.StatusCode);
        Assert.Equal(1, provider.Calls);
    }

    [Fact]
    public async Task GetResponseAsync_AFailureNoProviderProduced_FailsWithoutAskingAgain()
    {
        // Arrange
        var clock = new FakeTimeProvider();
        using var provider = new ScriptedProvider(clock, new InvalidOperationException("the answer could not be read"));
        using var client = new TransientProviderRetryChatClient(provider, clock);

        // Act
        await Assert.ThrowsAsync<InvalidOperationException>(() => AskAsync(client, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(1, provider.Calls);
    }

    [Fact]
    public async Task GetResponseAsync_TransientFailuresOnEveryAttempt_StopsAtTheBoundAndRethrowsTheLastFailure()
    {
        // Arrange
        var clock = new FakeTimeProvider();
        using var provider = new ScriptedProvider(clock, [.. Enumerable.Range(1, TransientProviderRetryChatClient.MaxAttempts)
            .Select(static attempt => new HttpRequestException($"throttled on attempt {attempt}", inner: null, HttpStatusCode.TooManyRequests))]);
        using var client = new TransientProviderRetryChatClient(provider, clock);

        // Act
        var failure = await Assert.ThrowsAsync<HttpRequestException>(
            () => AdvanceUntilCompletedAsync(clock, AskAsync(client, TestContext.Current.CancellationToken)));

        // Assert
        Assert.Equal($"throttled on attempt {TransientProviderRetryChatClient.MaxAttempts}", failure.Message);
        Assert.Equal(TransientProviderRetryChatClient.MaxAttempts, provider.Calls);
    }

    [Fact]
    public async Task GetResponseAsync_TheCallerCancels_FailsWithoutAskingAgain()
    {
        // Arrange
        var clock = new FakeTimeProvider();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        using var provider = new ScriptedProvider(clock, new OperationCanceledException(cancellation.Token));
        using var client = new TransientProviderRetryChatClient(provider, clock);

        // Act
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => AskAsync(client, cancellation.Token));

        // Assert
        Assert.Equal(1, provider.Calls);
    }

    private static Task<ChatResponse> AskAsync(TransientProviderRetryChatClient client, CancellationToken cancellationToken) =>
        client.GetResponseAsync([new ChatMessage(ChatRole.User, "Which build fixed the export?")], cancellationToken: cancellationToken);

    /// <summary>Moves the clock on until the call has finished waiting between its attempts.</summary>
    private static async Task<ChatResponse> AdvanceUntilCompletedAsync(FakeTimeProvider clock, Task<ChatResponse> call)
    {
        for (var step = 0; step < 100 && !call.IsCompleted; step++)
        {
            clock.Advance(TimeSpan.FromSeconds(30));

            await Task.Yield();
        }

        Assert.True(call.IsCompleted, "The call was still waiting after the clock had moved past every delay it could draw.");

        return await call;
    }

    /// <summary>A provider that fails with each scripted failure in turn and answers once they are spent.</summary>
    private sealed class ScriptedProvider(TimeProvider clock, params Exception[] failures) : IChatClient
    {
        public List<DateTimeOffset> AskedAt { get; } = [];

        public int Calls => this.AskedAt.Count;

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            this.AskedAt.Add(clock.GetUtcNow());

            return this.Calls <= failures.Length
                ? Task.FromException<ChatResponse>(failures[this.Calls - 1])
                : Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, Answer)));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
