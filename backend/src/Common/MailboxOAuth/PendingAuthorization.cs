// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Common.OAuth;

namespace MailFathom.Common.MailboxOAuth;

/// <summary>An authorization request that has been built and is waiting for the person to return its code.</summary>
/// <remarks>
/// This is the shape that makes a headless server workable without a browser anywhere near it. The person opens the
/// address on their own computer, signs in, and the authorization server redirects to a loopback address that nothing
/// is listening on. The browser then shows a connection error — and the address bar holds the authorization code,
/// which the operator pastes back. The failed redirect is the point rather than a defect: the code never leaves the
/// person's machine over the network.
/// </remarks>
public sealed record PendingAuthorization
{
    /// <summary>Initializes a pending authorization bound to one proof key.</summary>
    /// <param name="authorizationUrl">The address the person opens.</param>
    /// <param name="expectedState">The value the redirect must echo.</param>
    /// <param name="proofKey">The proof key the eventual code is redeemed with.</param>
    /// <remarks>The constructor is internal because only <see cref="MailboxAuthorizer" /> can produce a value whose proof key matches an authorization request it actually sent.</remarks>
    internal PendingAuthorization(Uri authorizationUrl, string expectedState, PkceCodeChallenge proofKey)
    {
        this.AuthorizationUrl = authorizationUrl;
        this.ExpectedState = expectedState;
        this.ProofKey = proofKey;
    }

    /// <summary>Gets the address the person opens, on whichever machine has a browser.</summary>
    public Uri AuthorizationUrl { get; }

    /// <summary>Gets the value the returned redirect must echo, which the command checks before redeeming.</summary>
    public string ExpectedState { get; }

    /// <summary>Gets the proof key this authorization is bound to.</summary>
    /// <remarks>
    /// The key stays inside the record instead of travelling through the caller. It is the secret half of the PKCE
    /// pair, it has to survive from building the address to redeeming the code, and a command that carried it would be
    /// holding a credential it has no use for.
    /// </remarks>
    internal PkceCodeChallenge ProofKey { get; }

    /// <summary>Reports whether a redirect echoed the value this authorization was issued with.</summary>
    /// <param name="returnedState">The <c>state</c> parameter the operator read back from the redirect address.</param>
    /// <returns><see langword="true" /> when the code may be redeemed against this authorization.</returns>
    /// <remarks>
    /// <para>
    /// This is the anti-forgery check, and it lives here rather than in the command that prompts for the value so that
    /// it is covered where every other rule about this exchange is. A command is a composition root; a security
    /// comparison written there would be reachable only through a console.
    /// </para>
    /// <para>
    /// The comparison is ordinal and case-sensitive against a value this process generated from cryptographically
    /// secure random material, and surrounding whitespace is removed because it comes from a copy and paste rather
    /// than from the authorization server. It is not a constant-time comparison: the expected value is not a secret
    /// the attacker is trying to learn — it is echoed back through the operator's own browser — and what it proves is
    /// that the code arrived from the authorization this process started.
    /// </para>
    /// </remarks>
    public bool MatchesReturnedState(string? returnedState) =>
        !string.IsNullOrWhiteSpace(returnedState)
        && string.Equals(returnedState.Trim(), this.ExpectedState, StringComparison.Ordinal);
}
