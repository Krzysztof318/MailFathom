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

    /// <summary>Raised when the signed-in identity changes: somebody signed in, or the session held here ended.</summary>
    /// <remarks>
    /// <para>
    /// What the rest of the client refreshes on. A deployment answers about the credential presented to it, so
    /// everything derived from that answer — what the caller may do, and therefore what the interface offers — is
    /// stale the moment this fires. Publishing the change here rather than leaving each reader to ask again is what
    /// keeps a screen from deciding it may not do something because nobody had signed in yet when it looked.
    /// </para>
    /// <para>
    /// It carries nothing. The token is not readable outside this assembly and a subscriber has no business knowing
    /// which identity replaced which; what it needs is that the answer it holds no longer describes this session.
    /// </para>
    /// </remarks>
    public event EventHandler? SignedInChanged;

    /// <summary>Gets whether somebody has signed in during this run.</summary>
    /// <remarks>Not whether the token is still accepted: only the deployment knows that, and it says so by refusing a request.</remarks>
    public bool IsSignedIn => this.current is not null;

    /// <summary>Gets the token to present, or <see langword="null" /> where nobody has signed in.</summary>
    internal string? Current => this.current;

    /// <summary>Takes the token a completed sign-in produced.</summary>
    /// <param name="accessToken">The issued token.</param>
    /// <exception cref="ArgumentException">Thrown when the token is blank.</exception>
    /// <remarks>
    /// Every accepted token is announced, without the one held before it being read to decide whether anything moved.
    /// A sign-in that produced a token is a new session whatever the bytes are, and comparing two credentials to save
    /// an announcement would be a secret comparison written for nothing.
    /// </remarks>
    internal void Accept(string accessToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);

        this.current = accessToken;

        this.SignedInChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Drops whatever is held, which ends the session without asking the deployment anything.</summary>
    /// <remarks>
    /// What <see cref="DeploymentAddress" /> calls when the client is pointed at another deployment. A token was issued
    /// by one authorization server for one deployment's audience, so carrying it to a second would present a credential
    /// that means nothing there — and the honest reading of a person moving to another deployment is that this session
    /// is over rather than that it travels.
    /// </remarks>
    internal void Forget()
    {
        var held = this.current is not null;

        this.current = null;

        if (held)
        {
            this.SignedInChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
