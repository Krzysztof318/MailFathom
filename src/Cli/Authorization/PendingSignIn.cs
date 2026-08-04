// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Common.OAuth;

namespace MailFathom.Cli.Authorization;

/// <summary>A sign-in that has been started and is waiting for the person's browser to come back with a code.</summary>
/// <remarks>
/// Its own type rather than the mailbox flow's <see cref="Common.MailboxOAuth.PendingAuthorization" />,
/// because the proof key must not cross an assembly boundary. The verifier is the secret half of the PKCE pair, and
/// keeping it internal to the assembly that both builds the authorization and redeems the code is what stops it from
/// becoming a value some other caller can read. The two records are the same three fields for the same specification;
/// what differs is who is allowed to see the third.
/// </remarks>
internal sealed record PendingSignIn
{
    /// <summary>Initializes a pending sign-in bound to one proof key.</summary>
    /// <param name="authorizationUrl">The address the person opens.</param>
    /// <param name="expectedState">The value the redirect must echo.</param>
    /// <param name="proofKey">The proof key the eventual code is redeemed with.</param>
    internal PendingSignIn(Uri authorizationUrl, string expectedState, PkceCodeChallenge proofKey)
    {
        this.AuthorizationUrl = authorizationUrl;
        this.ExpectedState = expectedState;
        this.ProofKey = proofKey;
    }

    /// <summary>Gets the address the person opens to approve the sign-in.</summary>
    internal Uri AuthorizationUrl { get; }

    /// <summary>Gets the value the returned redirect must echo, which is checked before anything is redeemed.</summary>
    internal string ExpectedState { get; }

    /// <summary>Gets the proof key this sign-in is bound to.</summary>
    internal PkceCodeChallenge ProofKey { get; }

    /// <summary>Reports whether a redirect echoed the value this sign-in was started with.</summary>
    /// <param name="returnedState">The <c>state</c> parameter the redirect carried.</param>
    /// <returns><see langword="true" /> when the code may be redeemed against this sign-in.</returns>
    /// <remarks>
    /// The comparison is ordinal and case-sensitive against a value this process generated from cryptographically
    /// secure random material. It is not constant-time, and does not need to be: the expected value is not a secret an
    /// attacker is trying to learn — it travels through the person's own browser — and what it proves is that the code
    /// arrived from the sign-in this process started rather than from one somebody else began.
    /// </remarks>
    internal bool MatchesReturnedState(string? returnedState) =>
        !string.IsNullOrWhiteSpace(returnedState)
        && string.Equals(returnedState.Trim(), this.ExpectedState, StringComparison.Ordinal);
}
