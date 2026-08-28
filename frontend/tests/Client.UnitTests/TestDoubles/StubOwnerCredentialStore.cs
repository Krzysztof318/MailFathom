// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Client.Backend.Authorization;

namespace MailFathom.Client.UnitTests.TestDoubles;

/// <summary>A head's credential store, in memory, with what it does about a write stated by the test.</summary>
/// <remarks>
/// The stand-in for the three operating-system stores under <c>Platforms/Desktop/</c>, none of which a unit host can
/// reach: one needs a Windows logon session, one a macOS login keychain, and one a session bus with a keyring on it.
/// What is testable is everything above them — what is written, what is read back, what a refusing store leads to, and
/// what reconciliation clears — which is what this makes reachable.
/// </remarks>
internal sealed class StubOwnerCredentialStore : IOwnerCredentialStore
{
    /// <summary>Initializes a store that keeps what it is given.</summary>
    /// <param name="persistence">What this store reports it can do, and what a write answers with.</param>
    /// <param name="held">What it is already holding, as a previous run would have left it.</param>
    internal StubOwnerCredentialStore(
        CredentialPersistence persistence = CredentialPersistence.Kept,
        KeptOwnerCredential? held = null)
    {
        this.Persistence = persistence;
        this.Held = held;
    }

    /// <inheritdoc />
    public CredentialPersistence Persistence { get; }

    /// <summary>Gets what the store is holding, which is what a test asserts a sign-in kept.</summary>
    internal KeptOwnerCredential? Held { get; private set; }

    /// <summary>Gets how many times the store was asked to hold nothing.</summary>
    internal int Cleared { get; private set; }

    /// <inheritdoc />
    public ValueTask<KeptOwnerCredential?> ReadAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(this.Held);

    /// <inheritdoc />
    public ValueTask<CredentialPersistence> WriteAsync(
        KeptOwnerCredential credential,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(credential);

        if (this.Persistence == CredentialPersistence.Kept)
        {
            this.Held = credential;
        }

        return ValueTask.FromResult(this.Persistence);
    }

    /// <inheritdoc />
    public ValueTask ClearAsync(CancellationToken cancellationToken = default)
    {
        this.Held = null;
        this.Cleared++;

        return ValueTask.CompletedTask;
    }
}
