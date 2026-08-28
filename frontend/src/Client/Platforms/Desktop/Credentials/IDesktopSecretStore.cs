// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Client.Platforms.Desktop.Credentials;

/// <summary>One operating system's own place for one person's secret, as this head reaches it.</summary>
/// <remarks>
/// <para>
/// Three implementations — the Windows Credential Manager, Keychain Services on macOS, and the Secret Service through
/// <c>libsecret</c> on Linux — behind one shape, so nothing above knows which platform answered. There is exactly one
/// entry: this application signs one person in to one deployment at a time, so a key would name a thing there is only
/// ever one of.
/// </para>
/// <para>
/// Every member throws <see cref="DesktopSecretStoreUnavailable" /> when this machine has no such store, when the
/// session cannot reach it, or when it refuses. That is one case rather than several because the answer to all of them
/// is the same: nothing is kept, the credential lives in memory for the run, and the person is told their next start
/// will ask again.
/// </para>
/// </remarks>
internal interface IDesktopSecretStore
{
    /// <summary>Gets whether this machine has a store to reach, judged without writing a secret to find out.</summary>
    /// <remarks>What the sign-in screen says up front. It is what this store expects rather than a promise: a keyring reachable now can be locked by the time something is written, which is what the write's own answer reports.</remarks>
    bool IsReachable { get; }

    /// <summary>Reads the entry.</summary>
    /// <returns>What it holds, or <see langword="null" /> where there is none.</returns>
    /// <exception cref="DesktopSecretStoreUnavailable">Thrown when there is no reachable store.</exception>
    string? Read();

    /// <summary>Writes the entry, replacing whatever it held.</summary>
    /// <param name="value">The document to hold.</param>
    /// <exception cref="DesktopSecretStoreUnavailable">Thrown when there is no reachable store.</exception>
    void Write(string value);

    /// <summary>Removes the entry.</summary>
    /// <exception cref="DesktopSecretStoreUnavailable">Thrown when there is no reachable store.</exception>
    void Clear();
}

/// <summary>This machine has no secret store this head can keep a credential in.</summary>
/// <remarks>
/// Absent, unreachable, locked, and refusing are one exception rather than four, because they have one answer here.
/// The message says which of them it was so that a diagnostic reads usefully; nothing composed from it reaches a
/// screen, which says one sentence about the next start rather than reporting a platform.
/// </remarks>
internal sealed class DesktopSecretStoreUnavailable : Exception
{
    /// <summary>Initializes the failure with why there is no store here.</summary>
    /// <param name="message">What happened, written for whoever reads a diagnostic.</param>
    internal DesktopSecretStoreUnavailable(string message)
        : base(message)
    {
    }

    /// <summary>Initializes the failure over the one that caused it.</summary>
    /// <param name="message">What happened, written for whoever reads a diagnostic.</param>
    /// <param name="innerException">The failure this stands for.</param>
    internal DesktopSecretStoreUnavailable(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
