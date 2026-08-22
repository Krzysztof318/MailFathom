// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Client.Backend.Authorization;

/// <summary>A sign-in that has been started and is waiting for the person's browser to come back with a code.</summary>
/// <remarks>
/// Internal, and the proof key with it. The verifier is the secret half of the PKCE pair, and keeping it inside the
/// assembly that both builds the authorization request and redeems the code is what stops it from becoming a value a
/// screen, a model, or a logger can reach.
/// </remarks>
internal sealed record PendingSignIn(Uri AuthorizationUrl, string ExpectedState, PkceCodeChallenge ProofKey)
{
    /// <summary>Reports whether a redirect echoed the value this sign-in was started with.</summary>
    /// <param name="returnedState">The <c>state</c> parameter the redirect carried.</param>
    /// <returns><see langword="true" /> when the code may be redeemed against this sign-in.</returns>
    /// <remarks>
    /// The comparison is ordinal and case-sensitive against a value this process generated from cryptographically secure
    /// random material. It is not constant-time, and does not need to be: the expected value is not a secret an attacker
    /// is trying to learn — it travels through the person's own browser — and what it proves is that the code arrived
    /// from the sign-in this process started rather than from one somebody else began.
    /// </remarks>
    internal bool MatchesReturnedState(string? returnedState) =>
        !string.IsNullOrWhiteSpace(returnedState)
        && string.Equals(returnedState.Trim(), this.ExpectedState, StringComparison.Ordinal);
}
