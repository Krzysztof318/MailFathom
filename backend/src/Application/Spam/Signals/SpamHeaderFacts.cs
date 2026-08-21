// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Spam.Signals;

/// <summary>Everything the deterministic stage reads out of one message's headers.</summary>
/// <remarks>
/// <para>
/// Both lists are bounded, because a message can carry a header field any number of times and the whole point of the
/// deterministic stage is that it costs nothing unbounded. What is dropped past a bound is the tail of a repetition
/// rather than a distinct fact: a message with more than <see cref="MaximumAuthenticationResults" /> outcomes is one
/// that travelled a long relay chain, and the newest hops are the ones written first.
/// </para>
/// <para>
/// Nothing here is loggable. An authentication detail names a sending domain and a provider header can carry the rule
/// names a scanner fired on the sender's own content.
/// </para>
/// </remarks>
public sealed record SpamHeaderFacts
{
    /// <summary>The greatest number of authentication outcomes read from one message.</summary>
    public const int MaximumAuthenticationResults = 32;

    /// <summary>The greatest number of provider spam headers read from one message.</summary>
    public const int MaximumProviderHeaders = 16;

    private SpamHeaderFacts(
        IReadOnlyList<MessageAuthenticationResult> authenticationResults,
        IReadOnlyList<ProviderSpamHeaderValue> providerHeaders)
    {
        this.AuthenticationResults = authenticationResults;
        this.ProviderHeaders = providerHeaders;
    }

    /// <summary>Gets the facts of a message that carried none of either.</summary>
    public static SpamHeaderFacts None { get; } = new([], []);

    /// <summary>Gets the sender-authentication outcomes the message carried, nearest hop first.</summary>
    public IReadOnlyList<MessageAuthenticationResult> AuthenticationResults { get; }

    /// <summary>Gets the provider spam headers the message carried, in the order they appeared.</summary>
    public IReadOnlyList<ProviderSpamHeaderValue> ProviderHeaders { get; }

    /// <summary>Records what one message's headers carried.</summary>
    /// <param name="authenticationResults">The authentication outcomes, nearest hop first.</param>
    /// <param name="providerHeaders">The provider spam headers, in the order they appeared.</param>
    /// <returns>The facts, with each list held to its bound.</returns>
    /// <exception cref="ArgumentNullException">Thrown when either argument is <see langword="null" />.</exception>
    public static SpamHeaderFacts Create(
        IReadOnlyList<MessageAuthenticationResult> authenticationResults,
        IReadOnlyList<ProviderSpamHeaderValue> providerHeaders)
    {
        ArgumentNullException.ThrowIfNull(authenticationResults);
        ArgumentNullException.ThrowIfNull(providerHeaders);

        return new SpamHeaderFacts(
            [.. authenticationResults.Take(MaximumAuthenticationResults)],
            [.. providerHeaders.Take(MaximumProviderHeaders)]);
    }
}
