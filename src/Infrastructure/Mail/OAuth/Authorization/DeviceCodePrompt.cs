// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Infrastructure.Mail.OAuth.Authorization;

/// <summary>What a person has to be shown to complete a device-code authorization on another device.</summary>
/// <param name="UserCode">The short code the person types at the verification address.</param>
/// <param name="VerificationUri">The address the person opens in a browser.</param>
/// <param name="VerificationUriComplete">The same address with the code already embedded, or <see langword="null" /> when the provider issued none.</param>
/// <param name="ExpiresAt">When the code stops being redeemable.</param>
/// <remarks>
/// The prompt is reported rather than printed, so the flow stays free of a console it does not own and the command
/// decides how an operator sees it. Nothing here is a credential: the user code is single-use, bound to one pending
/// authorization, and useless without the person signing in.
/// </remarks>
public sealed record DeviceCodePrompt(
    string UserCode,
    Uri VerificationUri,
    Uri? VerificationUriComplete,
    DateTimeOffset ExpiresAt);

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
}
