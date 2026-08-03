// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using MailFathom.Common;

namespace MailFathom.Cli;

/// <summary>Holds the key the credential store's tokens are sealed with, and applies it.</summary>
/// <remarks>
/// <para>
/// The sealing itself is <see cref="AesGcmEnvelope" />, which is shared. What lives here is the half that cannot be
/// shared: where the key comes from and what protects it, which follow from this being an operator's workstation rather
/// than a server. Each token is bound to its own endpoint, so a value moved between profiles in the file does not open.
/// </para>
/// <para>
/// <b>What this protects against.</b> The key is not a literal in this source, and that is deliberate: the repository
/// is public, so a key written here would be a key published permanently — it would obfuscate the file against a casual
/// reader and against nobody who looked it up. The key is instead random, generated on first use, and kept beside the
/// store with owner-only permissions; on Windows its file contents are additionally wrapped with DPAPI under the
/// current user, which binds them to that user on that machine.
/// </para>
/// <para>
/// So a credentials file that leaves the machine — in a backup, a synced folder, a support bundle, a screenshot of a
/// directory listing — discloses nothing on its own. Someone already able to read arbitrary files as this user on this
/// machine can read the key too, and no scheme that runs unattended can prevent that; the file mode is what answers
/// that case, and this answers the copy. Issue #318 replaces the arrangement with the platform's own secret service.
/// </para>
/// </remarks>
internal sealed class TokenProtector
{
    private readonly string keyPath;

    /// <summary>Initializes a protector over the key file beside a credential store.</summary>
    /// <param name="keyPath">The file the key is kept in, created on first use.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="keyPath" /> is <see langword="null" />.</exception>
    internal TokenProtector(string keyPath)
    {
        ArgumentNullException.ThrowIfNull(keyPath);

        this.keyPath = keyPath;
    }

    /// <summary>Seals a token for storage under one endpoint.</summary>
    /// <param name="token">The bearer credential.</param>
    /// <param name="endpoint">The endpoint the token belongs to, bound into the sealed value.</param>
    /// <returns>The value written to the store.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <exception cref="CliFailure">Thrown when the key cannot be read or created.</exception>
    internal string Protect(string token, string endpoint)
    {
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(endpoint);

        return AesGcmEnvelope.SealText(this.ReadOrCreateKey(), token, endpoint);
    }

    /// <summary>Opens a stored token.</summary>
    /// <param name="protectedToken">The value read from the store.</param>
    /// <param name="endpoint">The endpoint it was stored under.</param>
    /// <returns>The bearer credential.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <exception cref="CliFailure">Thrown when the value does not open, which is what a store copied from another machine looks like.</exception>
    internal string Unprotect(string protectedToken, string endpoint)
    {
        ArgumentNullException.ThrowIfNull(protectedToken);
        ArgumentNullException.ThrowIfNull(endpoint);

        var key = this.ReadOrCreateKey();

        try
        {
            return AesGcmEnvelope.OpenText(key, protectedToken, endpoint);
        }
        catch (Exception failure) when (failure is CryptographicException or FormatException)
        {
            throw new CliFailure(
                "The stored credential could not be read. That is what a credentials file copied from another machine or another user looks like. Sign in again to replace it.",
                failure);
        }
    }

    /// <summary>Reads the key, generating it on first use.</summary>
    /// <remarks>Generated rather than derived from anything nameable, so there is no material to reconstruct from knowing the machine. Created owner-only in one step, for the reason the store itself is.</remarks>
    private byte[] ReadOrCreateKey()
    {
        try
        {
            if (File.Exists(this.keyPath))
            {
                return Unwrap(File.ReadAllBytes(this.keyPath));
            }

            var key = AesGcmEnvelope.CreateKey();

            OwnerOnlyStorage.CreateDirectoryFor(this.keyPath);

            using (var contents = OwnerOnlyStorage.OpenForWriting(this.keyPath))
            {
                var wrapped = Wrap(key);
                contents.Write(wrapped, 0, wrapped.Length);
            }

            return key;
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or CryptographicException)
        {
            throw new CliFailure($"The credential key at {this.keyPath} could not be read or created.", failure);
        }
    }

    /// <summary>Binds the key to the current user where the platform can do it, and stores it plainly where it cannot.</summary>
    /// <remarks>
    /// DPAPI is the one mechanism either supported platform offers for this that needs no session, no daemon, and no
    /// passphrase. Linux has no equivalent a headless workstation can rely on, so there the key file's own mode is the
    /// boundary — the same boundary the credentials file already has, with the copy being what the sealing still
    /// answers.
    /// </remarks>
    private static byte[] Wrap(byte[] key) =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ProtectForCurrentUser(key) : key;

    private static byte[] Unwrap(byte[] stored) =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? UnprotectForCurrentUser(stored) : stored;

    [SupportedOSPlatform("windows")]
    private static byte[] ProtectForCurrentUser(byte[] key) =>
        ProtectedData.Protect(key, optionalEntropy: null, DataProtectionScope.CurrentUser);

    [SupportedOSPlatform("windows")]
    private static byte[] UnprotectForCurrentUser(byte[] stored) =>
        ProtectedData.Unprotect(stored, optionalEntropy: null, DataProtectionScope.CurrentUser);
}
