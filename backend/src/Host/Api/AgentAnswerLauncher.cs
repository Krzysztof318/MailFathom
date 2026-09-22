// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Access;
using MailFathom.Application.Agent.Answering;
using MailFathom.Application.Agent.Conversations;
using MailFathom.Application.Agent.Search;
using MailFathom.Application.Signals;
using MailFathom.Host.Security.Transport;

namespace MailFathom.Host.Api;

/// <summary>Composes the Agent's answer to one question past the request that asked it.</summary>
/// <remarks>
/// <para>
/// The answer belongs to no request: it is written into the conversation as it is composed, and every client reads it
/// from there. So it runs on a scope of its own that lives as long as the run, with the principal the transport admitted
/// stated onto it — the tools read and propose under exactly the grant the question was asked under, in the background
/// as in the foreground.
/// </para>
/// <para>
/// What ends it is a stop the person recorded, the run's own ceiling, or this process stopping — never a connection
/// closing, because a person who left the screen comes back to the conversation rather than to the request.
/// </para>
/// <para>
/// <strong>It is where a failure nothing could name is recorded.</strong> The use case ends the answer on every path it
/// reaches, and lets a fault it has no name for propagate after ending it; <c>Application</c> holds no logger, so the
/// fault is logged here. A scope this process could not compose the use case out of never reaches the use case at all,
/// so the answer is ended here instead, and nothing is left composing forever.
/// </para>
/// <para>
/// Once the answer has ended, what its turn said is embedded for the history search as a step of its own, so a failure
/// there is recorded as a lost ranking signal rather than as the failed answer it is not.
/// </para>
/// </remarks>
internal sealed partial class AgentAnswerLauncher
{
    private readonly IServiceScopeFactory scopeFactory;
    private readonly IAgentConversationStore store;
    private readonly ClientSignals signals;
    private readonly IUserLanguages languages;
    private readonly IHostApplicationLifetime lifetime;
    private readonly TimeProvider timeProvider;
    private readonly ILogger<AgentAnswerLauncher> logger;

    /// <summary>Initializes the launcher.</summary>
    /// <param name="scopeFactory">Makes the scope one run executes in.</param>
    /// <param name="store">Where the answer is ended when the run could not be started.</param>
    /// <param name="signals">Announces that ending.</param>
    /// <param name="languages">Resolves the language of the agent's own words in that ending.</param>
    /// <param name="lifetime">Reports that the process is stopping, which ends every run it is executing.</param>
    /// <param name="timeProvider">Stamps that ending.</param>
    /// <param name="logger">Reports the failures a run cannot name to its client.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public AgentAnswerLauncher(
        IServiceScopeFactory scopeFactory,
        IAgentConversationStore store,
        ClientSignals signals,
        IUserLanguages languages,
        IHostApplicationLifetime lifetime,
        TimeProvider timeProvider,
        ILogger<AgentAnswerLauncher> logger)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(signals);
        ArgumentNullException.ThrowIfNull(languages);
        ArgumentNullException.ThrowIfNull(lifetime);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        this.scopeFactory = scopeFactory;
        this.store = store;
        this.signals = signals;
        this.languages = languages;
        this.lifetime = lifetime;
        this.timeProvider = timeProvider;
        this.logger = logger;
    }

    /// <summary>Starts composing an answer and returns without waiting for it.</summary>
    /// <param name="question">The question the conversation recorded, and the answer it opened.</param>
    /// <param name="caller">The principal the transport admitted, which the run executes under.</param>
    /// <returns>The task this process finishes the run on, which the request that asked ignores.</returns>
    internal Task Start(AgentQuestion question, AuthorizedPrincipal caller)
    {
        ArgumentNullException.ThrowIfNull(question);
        ArgumentNullException.ThrowIfNull(caller);

        return Task.Run(() => this.ExecuteAsync(question, caller));
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Nothing awaits this task, so an escaping exception would be observed by nobody and could leave the answer composing; it is logged and the answer is ended instead.")]
    private async Task ExecuteAsync(AgentQuestion question, AuthorizedPrincipal caller)
    {
        var reached = false;

        try
        {
            await using var scope = this.scopeFactory.CreateAsyncScope();

            scope.ServiceProvider.GetRequiredService<TransportAuthorizedPrincipalSource>().Assume(caller);

            var answering = scope.ServiceProvider.GetRequiredService<AgentAnswering>();

            reached = true;
            await answering.RunAsync(question, this.lifetime.ApplicationStopping);
            await this.EmbedTurnAsync(scope.ServiceProvider, question);
        }
        catch (Exception failure)
        {
            this.LogRunFailedWithoutANameForIt(failure);

            if (!reached)
            {
                await this.EndFailedAsync(question);
            }
        }
    }

    /// <summary>Places what the turn said beside the stored vectors, once the answer it belongs to has ended.</summary>
    /// <remarks>
    /// Its own step rather than a part of the answer, because the answer has already been delivered by the time it runs:
    /// a failure here costs the history search its meaning for one turn, which is still found by its words, and is
    /// written down as that rather than as an answer that failed. A process that is stopping leaves it undone.
    /// </remarks>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "The answer has already ended; any failure placing its turn is a lost ranking signal to write down, never a failed answer.")]
    internal async Task EmbedTurnAsync(IServiceProvider services, AgentQuestion question)
    {
        try
        {
            var embedding = services.GetRequiredService<AgentConversationEmbedding>();

            await embedding.EmbedTurnAsync(question, this.lifetime.ApplicationStopping);
        }
        catch (OperationCanceledException) when (this.lifetime.ApplicationStopping.IsCancellationRequested)
        {
        }
        catch (Exception failure)
        {
            this.LogTurnCouldNotBeEmbedded(failure);
        }
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "This is the last thing that runs for an answer; a failure here is logged rather than escaping into a task nobody awaits.")]
    private async Task EndFailedAsync(AgentQuestion question)
    {
        try
        {
            using var journal = new AgentAnswerJournal(
                question.Conversation,
                question.User,
                question.Answer,
                question.OpenedAt,
                this.store,
                this.signals,
                this.languages,
                this.timeProvider);

            await journal.EndAsync(AgentAnswerOutcome.Failed, CancellationToken.None);
        }
        catch (Exception failure)
        {
            this.LogAnswerCouldNotBeEnded(failure);
        }
    }

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "An Agent answer ended on a failure it had no name for, so the conversation says only that it failed. "
            + "Neither the question nor anything it read is in this record; what failed is this deployment's own "
            + "composition or a dependency it called.")]
    private partial void LogRunFailedWithoutANameForIt(Exception failure);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "An Agent answer could not be started and its ending could not be written, so the conversation reads "
            + "as composing until a stop ends it. Neither the question nor anything it read is in this record; what "
            + "failed is the deployment's own store.")]
    private partial void LogAnswerCouldNotBeEnded(Exception failure);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "An Agent answer ended and was delivered, but what its turn said could not be embedded for the history "
            + "search, so that turn is found by its words alone. Neither the question nor the answer is in this record.")]
    private partial void LogTurnCouldNotBeEmbedded(Exception failure);
}
