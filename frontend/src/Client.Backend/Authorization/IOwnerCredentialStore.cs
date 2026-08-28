// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Client.Backend.Authorization;

/// <summary>How much of a sign-in survives the process on this head.</summary>
/// <remarks>
/// Three cases rather than two, because a person is owed a different sentence for each of the two that do not keep
/// anything: a head that never keeps a password is a property of the platform they chose, and a store that is there
/// but could not be reached is something they may be able to fix.
/// </remarks>
public enum CredentialPersistence
{
    /// <summary>The operating system holds the credential for this user, so the next start opens already signed in.</summary>
    Kept = 0,

    /// <summary>This head keeps no credential at all, whatever the machine it runs on.</summary>
    /// <remarks>The browser head, where every store is scoped to the page's origin rather than to a person, so anything running on that origin would read an owner's password.</remarks>
    NotOfferedOnThisHead = 1,

    /// <summary>This head would keep one, and this machine's store is absent, locked, or refusing.</summary>
    /// <remarks>A Linux session with no Secret Service provider and a keychain somebody declined to unlock are the two ordinary shapes of it. The credential stays in memory for the run, and nothing weaker is written instead.</remarks>
    StoreUnavailable = 2,
}

/// <summary>A sign-in kept where the operating system holds a secret for one user.</summary>
/// <param name="Deployment">The deployment the credential belongs to, which is what keeps it from ever being presented to another.</param>
/// <param name="Credential">The owner's username and password.</param>
/// <remarks>
/// The address travels with the credential rather than being remembered beside it, because reconciling the store
/// against wherever the client comes up pointed is the whole of how at most one item is ever held. An item read back
/// for a deployment the client is not pointed at is cleared rather than presented.
/// </remarks>
public sealed record KeptOwnerCredential(Uri Deployment, OwnerCredential Credential);

/// <summary>Where a head keeps the credential somebody signed in with, if it keeps one at all.</summary>
/// <remarks>
/// <para>
/// The port
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0018-where-the-client-keeps-its-sign-in-credential.md">ADR 0018</see>
/// asks of a head: the credential goes only where the operating system holds a secret for one user, the browser head
/// keeps none, and nothing here ever falls back to a file beside the binary or to
/// <c>ApplicationData.Current.LocalSettings</c>, which holds the deployment address and no secret.
/// </para>
/// <para>
/// Declared here and implemented under <c>frontend/src/Client/Platforms/</c>, because this assembly targets plain
/// <c>net10.0</c> and reaching a platform's own store is a head's work. <see cref="SignedInOwner" /> is the only thing
/// that reads a credential back out of one, so a screen never holds what a store returned.
/// </para>
/// <para>
/// At most one item is held. Every implementation therefore keys nothing: it holds the one entry this application
/// wrote, replaces it on a write, and removes it on a clear.
/// </para>
/// </remarks>
public interface IOwnerCredentialStore
{
    /// <summary>Gets what this head can do with a credential at all, before one has been offered to it.</summary>
    /// <remarks>
    /// Answerable without writing a secret, which is what makes it something a sign-in screen can say up front: a
    /// browser head is <see cref="CredentialPersistence.NotOfferedOnThisHead" /> by construction, and a desktop head
    /// reports <see cref="CredentialPersistence.StoreUnavailable" /> where it can already tell there is no store to
    /// reach. It is what this head expects rather than a promise — <see cref="WriteAsync" /> reports what actually
    /// happened.
    /// </remarks>
    CredentialPersistence Persistence { get; }

    /// <summary>Reads whatever this head is holding.</summary>
    /// <param name="cancellationToken">Abandons the read.</param>
    /// <returns>The kept credential, or <see langword="null" /> where nothing is held or what is held cannot be read.</returns>
    /// <remarks>An item that cannot be read or does not parse is the same answer as one that is not there: the deployment owns the credential, so the client signs in again rather than reporting a storage defect to somebody who cannot act on it.</remarks>
    ValueTask<KeptOwnerCredential?> ReadAsync(CancellationToken cancellationToken = default);

    /// <summary>Keeps a credential, replacing whatever was held.</summary>
    /// <param name="credential">The sign-in to keep.</param>
    /// <param name="cancellationToken">Abandons the write.</param>
    /// <returns>What became of it, which is what the person is told about their next start.</returns>
    /// <remarks>A store that refused is reported rather than thrown, because a refused write is not a failed sign-in: the person is signed in for this run and is owed a sentence about the next one.</remarks>
    ValueTask<CredentialPersistence> WriteAsync(
        KeptOwnerCredential credential,
        CancellationToken cancellationToken = default);

    /// <summary>Removes whatever this head is holding.</summary>
    /// <param name="cancellationToken">Abandons the removal.</param>
    /// <returns>A task completing once nothing is held, or once the store said it could not be reached.</returns>
    /// <remarks>Never throws for a store that is absent or refusing. Signing out has already ended the session in memory, and an entry a locked keyring will not give up is something only the person can clear.</remarks>
    ValueTask ClearAsync(CancellationToken cancellationToken = default);
}
