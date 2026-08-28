// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using MailFathom.Client.Backend;
using MailFathom.Client.Backend.Authorization;

namespace MailFathom.Client.Platforms.Desktop.Credentials;

/// <summary>Keeps the sign-in in whichever store the operating system this head is running on holds one in.</summary>
/// <remarks>
/// <para>
/// The desktop head is one target framework running on Windows, Linux, and macOS, and there is no cross-platform store
/// to take: Uno's own <c>PasswordVault</c> is marked unsupported on the Skia targets, so a head that renders through
/// Skia everywhere cannot rest on it. What this does is choose between the three operating systems' own stores and
/// present them as the one port <c>Client.Backend</c> declares —
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0018-where-the-client-keeps-its-sign-in-credential.md">ADR 0018</see>
/// is why each of the three is the one it is.
/// </para>
/// <para>
/// One entry, holding the deployment address, the username, and the password as one small JSON document. One entry
/// rather than three, because the three are meaningless apart: a store that took two of them and refused the third
/// would leave a sign-in split across a place nothing could read it back from whole.
/// </para>
/// <para>
/// A store that is absent, locked, or refusing is reported rather than worked around. Nothing here writes a file
/// beside the binary and nothing here writes <c>ApplicationData.Current.LocalSettings</c>, which holds the deployment
/// address and no secret: the credential stays in memory for the run, and the sign-in screen says so.
/// </para>
/// <para>
/// Every one of the three platform calls is synchronous, and one of them can take a person's time: a locked keychain or
/// keyring raises an unlock prompt and blocks until somebody answers it. So the call is made on the thread pool rather
/// than on whichever thread asked — at a start that is the thread the window is drawn on — and the wait on it is
/// bounded. Secret Service withdraws its own call after thirty seconds and reports what happened; Keychain Services and
/// the Credential Manager offer no cancellation at all, so what expires here is this head's willingness to wait rather
/// than the call itself, and it is deliberately the longer of the two so the store that can say why answers first. A
/// wait that ran out is the answer an unreachable store already gives: the credential stays in memory for the run, and
/// the sign-in screen says so.
/// </para>
/// </remarks>
internal sealed class DesktopOwnerCredentialStore : IOwnerCredentialStore
{
    /// <summary>How long any one platform store is waited on before this head stops waiting for it.</summary>
    private static readonly TimeSpan StoreDeadline = TimeSpan.FromSeconds(60);

    private readonly IDesktopSecretStore store;

    /// <summary>Initializes the store over one platform's own.</summary>
    /// <param name="store">The operating system's secret store, or the one that reports having none.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="store" /> is <see langword="null" />.</exception>
    internal DesktopOwnerCredentialStore(IDesktopSecretStore store)
    {
        ArgumentNullException.ThrowIfNull(store);

        this.store = store;
    }

    /// <inheritdoc />
    public CredentialPersistence Persistence => this.store.IsReachable
        ? CredentialPersistence.Kept
        : CredentialPersistence.StoreUnavailable;

    /// <summary>Reports the store the operating system this head is running on offers.</summary>
    /// <returns>The store, which reports keeping nothing on a platform whose own this head does not reach.</returns>
    internal static IOwnerCredentialStore ForThisOperatingSystem()
    {
        if (OperatingSystem.IsWindows())
        {
            return new DesktopOwnerCredentialStore(new WindowsCredentialManager());
        }

        if (OperatingSystem.IsMacOS())
        {
            return new DesktopOwnerCredentialStore(new KeychainServices());
        }

        return OperatingSystem.IsLinux()
            ? new DesktopOwnerCredentialStore(new SecretServiceKeyring())
            : UnkeptOwnerCredentialStore.Instance;
    }

    /// <inheritdoc />
    public async ValueTask<KeptOwnerCredential?> ReadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return Read(await this.WithinTheDeadlineAsync(this.store.Read, cancellationToken).ConfigureAwait(false));
        }
        catch (DesktopSecretStoreUnavailable)
        {
            // No store to read from is the same answer as a store holding nothing: the person signs in again. It is
            // not reported as a failure, because there is nothing they could do about it at the moment of reading.
            return null;
        }
    }

    /// <inheritdoc />
    public async ValueTask<CredentialPersistence> WriteAsync(
        KeptOwnerCredential credential,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(credential);

        var stored = new StoredSignIn(
            credential.Deployment.AbsoluteUri,
            credential.Credential.Username,
            credential.Credential.Password);

        var document = JsonSerializer.Serialize(stored, DesktopCredentialJsonContext.Default.StoredSignIn);

        try
        {
            await this.WithinTheDeadlineAsync(() => this.store.Write(document), cancellationToken)
                .ConfigureAwait(false);

            return CredentialPersistence.Kept;
        }
        catch (DesktopSecretStoreUnavailable)
        {
            // Reported rather than thrown: the sign-in itself succeeded, and what is left to say is that the next start
            // will ask again. Nothing weaker is written instead.
            return CredentialPersistence.StoreUnavailable;
        }
    }

    /// <inheritdoc />
    public async ValueTask ClearAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await this.WithinTheDeadlineAsync(this.store.Clear, cancellationToken).ConfigureAwait(false);
        }
        catch (DesktopSecretStoreUnavailable)
        {
            // An entry a locked keyring will not give up is the person's to clear, and the session has already ended
            // here whatever the store did. Reporting it would put a storage failure in front of somebody who has just
            // signed out and is no longer looking.
        }
    }

    /// <summary>Makes one platform call away from the thread that asked, and stops waiting for it after the deadline.</summary>
    /// <typeparam name="T">What the call reports.</typeparam>
    /// <param name="call">The platform call, which blocks its thread for as long as the operating system takes.</param>
    /// <param name="cancellationToken">Abandons the wait, which does not abandon the call.</param>
    /// <returns>What the call reported.</returns>
    /// <exception cref="DesktopSecretStoreUnavailable">Thrown when the deadline ran out before the store answered.</exception>
    /// <remarks>
    /// The call itself cannot be withdrawn from here — none of the three platform APIs takes a deadline, and the one
    /// that can cancel does it from inside. What ends is this side's wait, so the pool thread stays in the prompt until
    /// the operating system is done with it while the client goes on drawing and the person is told their next start
    /// will ask again.
    /// </remarks>
    private async ValueTask<T> WithinTheDeadlineAsync<T>(Func<T> call, CancellationToken cancellationToken)
    {
        try
        {
            return await Task.Run(call).WaitAsync(StoreDeadline, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException expired)
        {
            throw new DesktopSecretStoreUnavailable(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"this machine's secret store did not answer within {StoreDeadline.TotalSeconds:F0} seconds"),
                expired);
        }
    }

    /// <summary>Makes one platform call that reports nothing away from the thread that asked.</summary>
    /// <param name="call">The platform call, which blocks its thread for as long as the operating system takes.</param>
    /// <param name="cancellationToken">Abandons the wait, which does not abandon the call.</param>
    /// <returns>A task completing once the call has, or once the deadline ran out.</returns>
    private ValueTask WithinTheDeadlineAsync(Action call, CancellationToken cancellationToken) =>
        new(this.WithinTheDeadlineAsync(
            () =>
            {
                call();

                return true;
            },
            cancellationToken).AsTask());

    /// <summary>Reads one entry back, treating anything unreadable as nothing held.</summary>
    /// <remarks>
    /// An item written by another version, or truncated, or holding an address this client may not be pointed at, is
    /// the same case as an absent one — the deployment owns the credential, so the answer to all of them is to sign in
    /// again rather than to report a defect to somebody who cannot act on it.
    /// </remarks>
    private static KeptOwnerCredential? Read(string? held)
    {
        if (held is not { Length: > 0 })
        {
            return null;
        }

        try
        {
            var stored = JsonSerializer.Deserialize(held, DesktopCredentialJsonContext.Default.StoredSignIn);

            if (stored is null
                || !Uri.TryCreate(stored.Deployment, UriKind.Absolute, out var deployment)
                || DeploymentAddressRule.Judge(deployment) != DeploymentAddressRefusal.None)
            {
                return null;
            }

            return new KeptOwnerCredential(deployment, new OwnerCredential(stored.Username, stored.Password));
        }
        catch (JsonException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            // A document whose two halves are no longer a credential this client would compose — blank, or a username
            // carrying a colon — is one nothing could present, which is the same case as nothing being held.
            return null;
        }
    }

    /// <summary>What one entry holds, as the document it is written and read as.</summary>
    /// <param name="Deployment">The deployment the credential belongs to.</param>
    /// <param name="Username">The owner's username.</param>
    /// <param name="Password">The owner's password.</param>
    internal sealed record StoredSignIn(string Deployment, string Username, string Password)
    {
        /// <summary>Reports the document without what it holds.</summary>
        /// <returns>The type's name and nothing more.</returns>
        /// <remarks>The reasoning <see cref="OwnerCredential.ToString" /> carries, for the same reason: a positional record prints every member, so anything that renders this one — an interpolated message, a structured log's fallback formatter, a debugger watch — would otherwise carry the password.</remarks>
        public override string ToString() => nameof(StoredSignIn);
    }
}

/// <summary>The reader and writer for the one document a desktop store holds.</summary>
/// <remarks>Source-generated for the reason every serializer in this stack is: a reflection-based one is removed by the trimmer rather than reported, and <c>.config/BannedSymbols.txt</c> refuses those overloads outright.</remarks>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(DesktopOwnerCredentialStore.StoredSignIn))]
internal sealed partial class DesktopCredentialJsonContext : JsonSerializerContext;
