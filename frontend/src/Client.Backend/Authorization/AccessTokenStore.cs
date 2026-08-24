// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Client.Backend.Authorization;

/// <summary>Where this application's access token lives, which is in memory and nowhere else.</summary>
/// <remarks>
/// <para>
/// A field, for the process's lifetime, and that is the whole design. The token is written to no file, no browser
/// storage, and no platform credential store, so closing the application ends the session — and everything a MailFathom
/// deployment returns for it is somebody's correspondence, which the root instructions classify as personal data by
/// default. A token that reads that is treated the same way.
/// </para>
/// <para>
/// The token itself is not readable from outside this assembly. A screen has no business holding one, and the only
/// thing that needs it is the handler that attaches it to a request; what a screen may ask is whether anybody is signed
/// in, which is what <see cref="IsSignedIn" /> answers.
/// </para>
/// <para>
/// No refresh token is kept beside it, because none is asked for. A session that outlives its first access token is a
/// separate decision with its own privacy reasoning, and a credential held for a renewal this code never performs would
/// be a secret stored for nothing.
/// </para>
/// </remarks>
public sealed class AccessTokenStore
{
    private volatile string? current;

    /// <summary>Gets whether somebody has signed in during this run.</summary>
    /// <remarks>Not whether the token is still accepted: only the deployment knows that, and it says so by refusing a request.</remarks>
    public bool IsSignedIn => this.current is not null;

    /// <summary>Gets the token to present, or <see langword="null" /> where nobody has signed in.</summary>
    internal string? Current => this.current;

    /// <summary>Takes the token a completed sign-in produced.</summary>
    /// <param name="accessToken">The issued token.</param>
    /// <exception cref="ArgumentException">Thrown when the token is blank.</exception>
    internal void Accept(string accessToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);

        this.current = accessToken;
    }

    /// <summary>Drops whatever is held, which ends the session without asking the deployment anything.</summary>
    /// <remarks>
    /// What <see cref="DeploymentAddress" /> calls when the client is pointed at another deployment. A token was issued
    /// by one authorization server for one deployment's audience, so carrying it to a second would present a credential
    /// that means nothing there — and the honest reading of a person moving to another deployment is that this session
    /// is over rather than that it travels.
    /// </remarks>
    internal void Forget() => this.current = null;
}
