// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Chat;
using MailFathom.Application.Retrieval.AskMail;

namespace MailFathom.AI.Orchestration;

/// <summary>Refuses the next call of a run that has spent what one question may cost, and counts what every call it does make consumed.</summary>
/// <remarks>
/// <para>
/// A decorator for the reason the resilience one is: the framework holds one client for the length of a run and calls
/// it once per turn of the tool loop, so a ceiling on a run has to sit inside the client where every turn passes
/// through it. It is composed <em>outside</em> the resilience decorator deliberately — a call this deployment's own
/// ceiling refused never reached the endpoint, and recording it against the provider's health or its circuit would
/// report an outage that is not happening.
/// </para>
/// <para>
/// Two ledgers are written because they answer different questions. The run's is what stops this question, and the
/// period's is what stops the next one; a call charged to only the first would let a client spend a deployment's whole
/// allowance one bounded question at a time.
/// </para>
/// <para>
/// They are written at different moments, and that is what
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0031-dividing-singleton-work-between-replicas-with-a-leased-scope.md">ADR 0031</see>
/// decided. The run's is a field and is charged per call, because the next call is refused against it. The period's is
/// a row every replica writes, and it is charged once as the run ends, because a write per turn of the tool loop is the
/// cost that ADR refused — a mailbox backfill turns this loop once per message. What is accumulated meanwhile is the
/// run's own total, so a run that failed, was refused, or was cancelled part way through still charges the period with
/// what it actually spent: disposal happens on every one of those paths.
/// </para>
/// <para>
/// A provider that reports no usage advances neither token count. The call ceilings above and beside this one are what
/// hold in that case, which is why both exist.
/// </para>
/// </remarks>
internal sealed class BudgetedChatClient : Microsoft.Extensions.AI.DelegatingChatClient, IAsyncDisposable
{
    private readonly MailAnsweringRunLedger runLedger;
    private readonly IMailAnsweringSpendLedger spendLedger;
    private readonly Lock gate = new();
    private long unchargedInputTokens;
    private long unchargedOutputTokens;
    private int settled;

    /// <summary>Initializes the decorator over the client one run sends through.</summary>
    /// <param name="innerClient">The client every admitted call is delegated to.</param>
    /// <param name="runLedger">Counts what this run has spent and refuses the call that would take it past its ceiling.</param>
    /// <param name="spendLedger">Counts what the current period has spent across every run.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    internal BudgetedChatClient(
        Microsoft.Extensions.AI.IChatClient innerClient,
        MailAnsweringRunLedger runLedger,
        IMailAnsweringSpendLedger spendLedger)
        : base(innerClient)
    {
        ArgumentNullException.ThrowIfNull(runLedger);
        ArgumentNullException.ThrowIfNull(spendLedger);

        this.runLedger = runLedger;
        this.spendLedger = spendLedger;
    }

    /// <inheritdoc />
    /// <exception cref="MailAnsweringBudgetExhaustedException">Thrown when the run has spent what one question may cost, before anything is sent.</exception>
    public override async Task<Microsoft.Extensions.AI.ChatResponse> GetResponseAsync(
        IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages,
        Microsoft.Extensions.AI.ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        this.runLedger.RequireAllowanceForNextCall();

        var response = await base.GetResponseAsync(messages, options, cancellationToken);

        if (response.Usage is { } reported)
        {
            var usage = new ChatTokenUsage(reported.InputTokenCount ?? 0, reported.OutputTokenCount ?? 0);

            this.runLedger.RecordSpend(usage);

            lock (this.gate)
            {
                this.unchargedInputTokens += usage.InputTokens;
                this.unchargedOutputTokens += usage.OutputTokens;
            }
        }

        return response;
    }

    /// <summary>Charges the period what this run consumed, once, and releases the client the run sent through.</summary>
    /// <returns>A task that completes when the period holds this run's tokens.</returns>
    /// <remarks>
    /// <para>
    /// The charge is here rather than beside each call because this decorator's lifetime <em>is</em> the run's: the
    /// framework holds one client for the length of a tool loop, so disposing it is the one moment that happens once
    /// per run and happens however the run ended.
    /// </para>
    /// <para>
    /// Not the caller's cancellation token, for the reason <c>StoredContentClaim.DisposeAsync</c> gives about its own:
    /// the provider calls have already been answered and already been paid for, so a client that aborts its request
    /// between the last answer and this write would leave a spend that happened uncounted — repeatably, which is a
    /// ceiling that fails open rather than a ceiling.
    /// </para>
    /// </remarks>
    public async ValueTask DisposeAsync()
    {
        long inputTokens;
        long outputTokens;

        lock (this.gate)
        {
            inputTokens = this.unchargedInputTokens;
            outputTokens = this.unchargedOutputTokens;
        }

        if (inputTokens + outputTokens > 0 && Interlocked.Exchange(ref this.settled, 1) == 0)
        {
            await this.spendLedger.RecordSpendAsync(
                new ChatTokenUsage(inputTokens, outputTokens),
                CancellationToken.None);
        }

        this.Dispose();
    }

    /// <inheritdoc />
    /// <remarks>Refused here as well as underneath, so no path exists on which a run could stream past the ceilings this decorator applies.</remarks>
    /// <exception cref="NotSupportedException">Always thrown.</exception>
    public override IAsyncEnumerable<Microsoft.Extensions.AI.ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages,
        Microsoft.Extensions.AI.ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(
            "Streaming a chat response is not supported: the spend ceilings this deployment applies are written for a call that returns one answer.");
}
