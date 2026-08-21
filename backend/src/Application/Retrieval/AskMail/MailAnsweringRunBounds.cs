// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;

namespace MailFathom.Application.Retrieval.AskMail;

/// <summary>What one answering run may draw out of the mailbox and spend at the provider before it is stopped.</summary>
/// <remarks>
/// <para>
/// A run is a conversation rather than a call: the model asks for mail, the tool answers, and the model may ask again.
/// <see cref="EmailKnowledgeBounds" /> bounds one lookup and the endpoint's own declaration bounds one request, so
/// neither of them says anything about a run that makes twenty lookups. These three do, and each is checked before the
/// next provider call rather than reported after the run.
/// </para>
/// <para>
/// Three numbers because they fail in three different ways. The retrieved-character ceiling is the privacy one — it is
/// the total amount of somebody's mail that may leave the process to answer one question, whatever the model asks for.
/// The token ceiling is the cost one, and it is the only one stated in the unit a provider bills by. The call ceiling
/// is the one that always works: a token count is what the provider reported, and an endpoint that reports none would
/// leave the cost ceiling unreachable while a tool loop went round.
/// </para>
/// <para>
/// The retrieval ceiling cuts and the other two stop the run. A lookup refused for budget still leaves an answerable
/// question — the run has mail already and is told there is no more — while a run that may make no further call has no
/// answer to publish and nothing to cut down to.
/// </para>
/// </remarks>
public sealed record MailAnsweringRunBounds
{
    private MailAnsweringRunBounds(int maximumRetrievedCharacters, int maximumProviderCalls, long maximumTokens)
    {
        this.MaximumRetrievedCharacters = maximumRetrievedCharacters;
        this.MaximumProviderCalls = maximumProviderCalls;
        this.MaximumTokens = maximumTokens;
    }

    /// <summary>Gets the bounds a deployment that states none receives.</summary>
    /// <remarks>
    /// Deliberately conservative. Twenty thousand characters is a little over two full lookups under the default
    /// retrieval bounds, eight calls is a tool loop that asked for mail several times and then wrote an answer, and
    /// eighty thousand tokens is what those eight calls cost when each carries everything the ones before it retrieved.
    /// An operator who wants a question to range further raises them knowing what each buys.
    /// </remarks>
    public static MailAnsweringRunBounds Default { get; } = new(20_000, 8, 80_000);

    /// <summary>Gets the greatest number of characters of retrieved mail one run may send.</summary>
    public int MaximumRetrievedCharacters { get; }

    /// <summary>Gets the greatest number of provider calls one run may make.</summary>
    public int MaximumProviderCalls { get; }

    /// <summary>Gets the greatest number of tokens, sent and received together, one run may consume.</summary>
    public long MaximumTokens { get; }

    /// <summary>Creates bounds, refusing values no run could complete under.</summary>
    /// <param name="maximumRetrievedCharacters">The greatest number of characters of retrieved mail one run may send.</param>
    /// <param name="maximumProviderCalls">The greatest number of provider calls one run may make.</param>
    /// <param name="maximumTokens">The greatest number of tokens one run may consume.</param>
    /// <returns>The validated bounds.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when a value is below one.</exception>
    /// <remarks>
    /// A call ceiling of one is accepted and is a usable deployment: the model answers from whatever the first call
    /// produces, having retrieved nothing. It is the floor rather than a recommendation.
    /// </remarks>
    public static MailAnsweringRunBounds Create(
        int maximumRetrievedCharacters,
        int maximumProviderCalls,
        long maximumTokens)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumRetrievedCharacters, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumProviderCalls, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumTokens, 1);

        return new MailAnsweringRunBounds(maximumRetrievedCharacters, maximumProviderCalls, maximumTokens);
    }

    /// <inheritdoc />
    public override string ToString() => string.Format(
        CultureInfo.InvariantCulture,
        "at most {0} retrieved characters over {1} calls costing at most {2} tokens",
        this.MaximumRetrievedCharacters,
        this.MaximumProviderCalls,
        this.MaximumTokens);
}
