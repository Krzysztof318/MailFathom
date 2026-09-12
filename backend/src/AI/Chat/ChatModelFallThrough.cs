// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics;
using MailFathom.AI.ProviderAdapters;
using MailFathom.Application.Chat;
using Microsoft.Extensions.Logging;

namespace MailFathom.AI.Chat;

/// <summary>Runs one call against the model a plan names, and against the fallback behind it where that model could not answer.</summary>
/// <remarks>
/// <para>
/// One place rather than one per caller. Nine callers open a chat client from a plan, and a fall-through written at each
/// of them would be nine copies of the rule that decides which failures are worth a second model — which is exactly the
/// rule that must not differ between the pass a reader waits for and the run that answers a question.
/// </para>
/// <para>
/// It wraps the whole attempt rather than the request inside it, because a second model is a second address with a
/// second credential over a second transport: what is retried is opening a client and asking, not re-sending a message
/// down a connection that failed.
/// </para>
/// </remarks>
internal static class ChatModelFallThrough
{
    /// <summary>Runs the attempt against each model of the chain until one answers.</summary>
    /// <typeparam name="TResult">What one attempt produces.</typeparam>
    /// <param name="plan">The plan naming the model to ask and the fallback behind it.</param>
    /// <param name="logger">Where a fall-through is recorded, so an operator sees that the second model answered.</param>
    /// <param name="attempt">Asks one model, raising <see cref="ChatGenerationFailedException" /> where it could not answer.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>What the model that answered produced.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <exception cref="ChatGenerationFailedException">Thrown with the last model's own failure when no model of the chain answered.</exception>
    public static async Task<TResult> RunAsync<TResult>(
        ChatGenerationPlan plan,
        ILogger logger,
        Func<ChatGenerationPlan, CancellationToken, Task<TResult>> attempt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(attempt);

        foreach (var candidate in plan.Chain)
        {
            try
            {
                return await attempt(candidate, cancellationToken);
            }
            catch (ChatGenerationFailedException failure)
                when (candidate.Fallback is { } fallback && IsWorthAnotherModel(failure))
            {
                ChatProviderEvents.LogFallingThroughToFallback(
                    logger,
                    candidate.Endpoint.Alias,
                    failure.Failure,
                    fallback.Endpoint.Alias);
            }
        }

        // The guard above catches only where a fallback exists, so the last model's failure leaves the loop rather than
        // reaching here, and a chain is never empty.
        throw new UnreachableException("A chat model chain answered with neither a result nor a failure.");
    }

    /// <summary>Reports whether the fallback model could answer where this one did not.</summary>
    /// <param name="failure">What ended the attempt.</param>
    /// <returns><see langword="true" /> when the failure is about the endpoint rather than about the request.</returns>
    /// <remarks>
    /// An unreachable endpoint, a throttled one, a slow one, and one that refused the credential are all statements
    /// about that endpoint, and the fallback is a different address with a different credential. The two that are not
    /// are a request the provider *refused*, which a second endpoint refuses the same way for a second payment, and an
    /// answer that came back empty, which is a call that succeeded and a model that had nothing to say.
    /// </remarks>
    public static bool IsWorthAnotherModel(ChatGenerationFailedException failure)
    {
        ArgumentNullException.ThrowIfNull(failure);

        return failure.Failure is not (ChatGenerationFailure.RequestRefused or ChatGenerationFailure.AnswerEmpty);
    }
}
