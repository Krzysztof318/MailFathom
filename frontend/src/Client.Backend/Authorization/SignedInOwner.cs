// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Client.Backend.Authorization;

/// <summary>Who is signed in during this run, and the credential presented on their behalf.</summary>
/// <remarks>
/// <para>
/// A field for the process's lifetime, and — where the head keeps one — one entry in the store the operating system
/// holds for this user. Nothing else: no file beside the binary, no browser storage, and never
/// <c>ApplicationData.Current.LocalSettings</c>, which holds the deployment address and no secret.
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0018-where-the-client-keeps-its-sign-in-credential.md">ADR 0018</see>
/// is the whole of that decision.
/// </para>
/// <para>
/// The credential is not readable from outside this assembly. A screen has no business holding one, and the only thing
/// that needs it is the handler that composes the header; what a screen may ask is whether anybody is signed in and
/// under which name, which is what <see cref="IsSignedIn" /> and <see cref="Username" /> answer.
/// </para>
/// <para>
/// At most one credential is held, for the deployment the client is pointed at. That takes three rules rather than one,
/// because the address can move without this process seeing it and can be absent altogether: pointing the client
/// elsewhere forgets it, a start reconciles what is stored against wherever the client came up pointed, and a start
/// that resolved no address at all forgets whatever was held. All three are the same reason — a credential for a
/// deployment nothing will ever present it to is a password kept for nothing.
/// </para>
/// </remarks>
public sealed class SignedInOwner
{
    private readonly IOwnerCredentialStore store;
    private readonly Lock guard = new();
    private OwnerCredential? current;

    /// <summary>Initializes the session over the place this head keeps a credential, if it keeps one.</summary>
    /// <param name="store">Where the credential outlives the process, or the store that keeps nothing.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="store" /> is <see langword="null" />.</exception>
    public SignedInOwner(IOwnerCredentialStore store)
    {
        ArgumentNullException.ThrowIfNull(store);

        this.store = store;
    }

    /// <summary>Raised when the signed-in identity changes: somebody signed in, or the session held here ended.</summary>
    /// <remarks>
    /// <para>
    /// What the rest of the client refreshes on. A deployment answers about the credential presented to it, so
    /// everything derived from that answer — what the caller may do, and therefore what the interface offers — is
    /// stale the moment this fires. Publishing the change here rather than leaving each reader to ask again is what
    /// keeps a screen from deciding it may not do something because nobody had signed in yet when it looked.
    /// </para>
    /// <para>
    /// It carries nothing. The credential is not readable outside this assembly and a subscriber has no business
    /// knowing which identity replaced which; what it needs is that the answer it holds no longer describes this
    /// session.
    /// </para>
    /// </remarks>
    public event EventHandler? SignedInChanged;

    /// <summary>Gets what this head does with a credential once somebody has signed in.</summary>
    /// <remarks>Read by the sign-in screen so that a head keeping nothing says why it will ask again rather than simply asking.</remarks>
    public CredentialPersistence Persistence => this.store.Persistence;

    /// <summary>Gets whether somebody is signed in.</summary>
    /// <remarks>Not whether the deployment still accepts them: only the deployment knows that, and it says so by refusing a request.</remarks>
    public bool IsSignedIn => this.Current is not null;

    /// <summary>Gets the username somebody is signed in under, or <see langword="null" /> where nobody is.</summary>
    /// <remarks>
    /// The half of the credential that is not a secret, and the only half anything outside this assembly is given. A
    /// person running a client against more than one deployment has to be able to see who they are before deciding to
    /// sign out, and a screen that had to ask the deployment for that would be asking for something the session route
    /// deliberately never reports.
    /// </remarks>
    public string? Username => this.Current?.Username;

    /// <summary>Gets the credential to present, or <see langword="null" /> where nobody has signed in.</summary>
    internal OwnerCredential? Current
    {
        get
        {
            lock (this.guard)
            {
                return this.current;
            }
        }
    }

    /// <summary>Takes a credential the deployment has just accepted, and keeps it where this head keeps one.</summary>
    /// <param name="deployment">The deployment it was accepted by, which the stored item is reconciled against later.</param>
    /// <param name="credential">The accepted credential.</param>
    /// <param name="cancellationToken">Abandons the write, which does not abandon the sign-in.</param>
    /// <returns>What became of keeping it, which is what the person is told about their next start.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <remarks>
    /// Held in memory before it is written, so a store that refuses leaves somebody signed in for this run rather than
    /// not signed in at all. Every accepted credential is announced, without the one held before it being read to
    /// decide whether anything moved: a sign-in is a new session whatever the characters are, and comparing two
    /// credentials to save an announcement would be a secret comparison written for nothing.
    /// </remarks>
    internal async ValueTask<CredentialPersistence> AcceptAsync(
        Uri deployment,
        OwnerCredential credential,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(deployment);
        ArgumentNullException.ThrowIfNull(credential);

        lock (this.guard)
        {
            this.current = credential;
        }

        this.SignedInChanged?.Invoke(this, EventArgs.Empty);

        return await this.store
            .WriteAsync(new KeptOwnerCredential(deployment, credential), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Ends the session: drops what is held and clears what was kept.</summary>
    /// <param name="cancellationToken">Abandons the removal, which does not un-end the session.</param>
    /// <returns>A task completing once nothing is held here and the store has been asked to hold nothing either.</returns>
    /// <remarks>
    /// What signing out does, what pointing the client at another deployment does, and what a deployment refusing a
    /// credential it once accepted leads to. It asks the deployment nothing: HTTP Basic has no server-side session to
    /// end, so the password stays valid there until an administrator rotates it, and the interface must not offer this
    /// as though it were revocation.
    /// </remarks>
    internal async ValueTask ForgetAsync(CancellationToken cancellationToken = default)
    {
        bool held;

        lock (this.guard)
        {
            held = this.current is not null;
            this.current = null;
        }

        // Cleared whether or not anything was held in memory, because a start that restored nothing may still be
        // sitting on an item written by a previous run.
        await this.store.ClearAsync(cancellationToken).ConfigureAwait(false);

        if (held)
        {
            this.SignedInChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Reconciles what this head kept against the deployment it has come up pointed at.</summary>
    /// <param name="pointedAt">Where the client is pointed, or <see langword="null" /> where nothing has pointed it.</param>
    /// <param name="cancellationToken">Abandons the read.</param>
    /// <returns><see langword="true" /> where a kept credential was restored, so the client opens already signed in.</returns>
    /// <remarks>
    /// Run on every start rather than only on one that resolved an address, which is what makes the third of the three
    /// rules above real: a client pointed nowhere is pointed at no deployment, so an item it holds belongs to nowhere
    /// it is going and is cleared with the address that was already forgotten.
    /// </remarks>
    internal async ValueTask<bool> RestoreAsync(Uri? pointedAt, CancellationToken cancellationToken = default)
    {
        var kept = await this.store.ReadAsync(cancellationToken).ConfigureAwait(false);

        if (kept is null)
        {
            return false;
        }

        if (pointedAt is null || kept.Deployment != pointedAt)
        {
            await this.store.ClearAsync(cancellationToken).ConfigureAwait(false);

            return false;
        }

        lock (this.guard)
        {
            this.current = kept.Credential;
        }

        this.SignedInChanged?.Invoke(this, EventArgs.Empty);

        return true;
    }
}
