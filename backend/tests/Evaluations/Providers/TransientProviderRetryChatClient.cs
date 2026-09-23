// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.ProviderAdapters;
using MailFathom.Application.Resilience;
using Microsoft.Extensions.AI;

namespace MailFathom.Evaluations.Providers;

/// <summary>Asks a provider again when a call failed because the provider was busy or unreachable, and never otherwise.</summary>
/// <remarks>
/// <para>
/// The call is the unit retried rather than the test, because only the call knows why it failed. A rate limit, a request
/// that timed out, and a transport fault say nothing about the model, so each is asked again after a jittered delay, as
/// <see cref="ProviderCallFailureClassification" /> sorts them for a deployment. A refused request and a rejected
/// credential would be refused again, and an answer that falls short is not a failure of the call at all, so both reach
/// the scenario at once and a shortfall is paid for once.
/// </para>
/// <para>
/// A request abandoned at the transport's own timeout arrives as a cancellation the caller never asked for, which the
/// classifier leaves unclassified; it is read as a timeout here, the way a deployment's resilient client reads it. The
/// caller's own cancellation is never retried.
/// </para>
/// <para>
/// Only the call that returns one answer is retried: nothing in the suite streams, so a streamed call passes through
/// unchanged.
/// </para>
/// </remarks>
/// <param name="innerClient">The provider's client every attempt is sent through.</param>
/// <param name="timeProvider">The clock the delay between attempts is measured on.</param>
internal sealed class TransientProviderRetryChatClient(IChatClient innerClient, TimeProvider timeProvider)
    : DelegatingChatClient(innerClient)
{
    /// <summary>How many times one call is sent before its failure is reported.</summary>
    public const int MaxAttempts = 3;

    /// <summary>The delay the first retry is drawn around, sized for a rate limit or a momentary overload to clear.</summary>
    private static readonly TimeSpan BaseDelay = TimeSpan.FromSeconds(5);

    /// <summary>The longest any one wait between attempts may grow to.</summary>
    private static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(30);

    /// <inheritdoc />
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        // Materialized once, because a sequence the caller built is not promised to survive a second enumeration.
        var conversation = messages as IReadOnlyList<ChatMessage> ?? [.. messages];

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await base.GetResponseAsync(conversation, options, cancellationToken);
            }
            catch (Exception failure) when (attempt < MaxAttempts && IsTransient(failure, cancellationToken))
            {
                await Task.Delay(
                    JitteredRetryBackoff.DelayBeforeNextAttempt(BaseDelay, MaxDelay, minimumDelay: TimeSpan.Zero, attempt),
                    timeProvider,
                    cancellationToken);
            }
        }
    }

    /// <summary>Whether a failure is one the provider would likely not repeat.</summary>
    private static bool IsTransient(Exception failure, CancellationToken cancellationToken) =>
        failure is OperationCanceledException
            ? !cancellationToken.IsCancellationRequested
            : ProviderCallFailureClassification.Classify(failure)
                is ProviderCallFailure.RateLimited
                or ProviderCallFailure.RequestTimedOut
                or ProviderCallFailure.TransportFaulted;
}
