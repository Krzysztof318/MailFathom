// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Cli.Credentials.SecretStores;

/// <summary>The operating system's own place for one person's secrets, as this command reaches it.</summary>
/// <remarks>
/// <para>
/// One port with two implementations — the Windows Credential Manager and the Secret Service through <c>libsecret</c> —
/// and a third that reports having no store at all. The port is what
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0018-where-the-client-keeps-its-sign-in-credential.md">ADR 0018</see>
/// asks of a command: the credential goes where the operating system holds a secret for one user, and nothing that
/// reaches it here knows which platform answered.
/// </para>
/// <para>
/// Every member throws <see cref="SecretStoreUnavailable" /> when this machine has no such store, when the session
/// cannot reach it, or when it refuses. That is one case rather than several, because the answer to all of them is the
/// same: the command falls back to the sealed credentials file and says so. Only a store that answered decides
/// anything, so a caller that catches the exception is choosing the weaker storage deliberately rather than by
/// accident.
/// </para>
/// </remarks>
internal interface IOperatorSecretStore
{
    /// <summary>Gets the store's name as a sentence to an operator would write it, such as <c>the Windows Credential Manager</c>.</summary>
    string Description { get; }

    /// <summary>Reads one secret.</summary>
    /// <param name="secret">Which profile's secret to read.</param>
    /// <returns>The value, or <see langword="null" /> when the store holds no such entry.</returns>
    /// <exception cref="SecretStoreUnavailable">Thrown when this machine has no reachable store.</exception>
    string? Read(ProfileSecret secret);

    /// <summary>Writes one secret, replacing whatever the entry held.</summary>
    /// <param name="secret">Which profile's secret to write.</param>
    /// <param name="value">The value to hold.</param>
    /// <exception cref="SecretStoreUnavailable">Thrown when this machine has no reachable store.</exception>
    void Write(ProfileSecret secret, string value);

    /// <summary>Removes one secret.</summary>
    /// <param name="secret">Which profile's secret to remove.</param>
    /// <returns><see langword="true" /> when an entry was removed, <see langword="false" /> when there was none.</returns>
    /// <exception cref="SecretStoreUnavailable">Thrown when this machine has no reachable store.</exception>
    bool Clear(ProfileSecret secret);
}

/// <summary>Names one secret a profile keeps outside its file.</summary>
/// <remarks>
/// <para>
/// Keyed by the deployment's address rather than by the profile's name, because the name is the operator's and the
/// address is the deployment's: two names for one deployment are one credential, and renaming a profile must not
/// orphan the entry holding its token. It is also what keeps one deployment's credential from ever being presented to
/// another, which a key derived from a name could not promise.
/// </para>
/// <para>
/// Constructed only through the two factories, so the set of secrets a profile can keep is closed at exactly the two
/// it has: the bearer credential it presents, and the refresh token an OAuth session renews that credential with.
/// </para>
/// </remarks>
internal sealed record ProfileSecret
{
    private ProfileSecret(string address, string kind)
    {
        this.Address = address;
        this.Kind = kind;
    }

    /// <summary>Gets the deployment address this secret belongs to.</summary>
    internal string Address { get; }

    /// <summary>Gets which of a profile's two secrets this is, as the entry's own name records it.</summary>
    internal string Kind { get; }

    /// <summary>Names the bearer credential a profile presents.</summary>
    /// <param name="address">The deployment address the profile holds.</param>
    /// <returns>The name of that profile's token entry.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="address" /> is <see langword="null" />.</exception>
    internal static ProfileSecret BearerToken(string address)
    {
        ArgumentNullException.ThrowIfNull(address);

        return new ProfileSecret(address, "token");
    }

    /// <summary>Names the refresh token an OAuth profile renews its access token with.</summary>
    /// <param name="address">The deployment address the profile holds.</param>
    /// <returns>The name of that profile's refresh-token entry.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="address" /> is <see langword="null" />.</exception>
    internal static ProfileSecret RefreshToken(string address)
    {
        ArgumentNullException.ThrowIfNull(address);

        return new ProfileSecret(address, "refresh-token");
    }
}

/// <summary>This machine has no secret store the command can put a credential in.</summary>
/// <remarks>
/// Absent, unreachable, locked, and refusing are one exception rather than four, because they have one answer. The
/// message says which of them it was, so the sentence the operator reads names the thing they could change — installing
/// <c>libsecret</c>, starting a session bus, unlocking a collection — rather than reporting that storage failed.
/// </remarks>
internal sealed class SecretStoreUnavailable : Exception
{
    /// <summary>Initializes the failure with what the operator reads.</summary>
    /// <param name="message">Why there is no store here, written for someone deciding whether to act on it.</param>
    internal SecretStoreUnavailable(string message)
        : base(message)
    {
    }

    /// <summary>Initializes the failure over the one that caused it.</summary>
    /// <param name="message">Why there is no store here, written for someone deciding whether to act on it.</param>
    /// <param name="innerException">The failure this stands for.</param>
    internal SecretStoreUnavailable(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
