// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Secrets.References;

namespace MailFathom.Infrastructure.DataEncryption;

/// <summary>Resolves the deployment's data-encryption keys, one operation at a time.</summary>
/// <remarks>
/// <para>
/// Nothing is cached. A key is resolved from its reference for each operation that needs one and erased when that
/// operation ends, which is the same rule every other credential in this system follows: material rotated behind an
/// unchanged reference is observed by the next operation, with no cache to invalidate, and the window in which a process
/// dump could contain a key is bounded by an operation rather than by uptime.
/// </para>
/// <para>
/// The settings arrive through a delegate rather than as a value, so the ring reads the currently published snapshot
/// each time. A key an operator adds to a running deployment is therefore available to the next operation, which is what
/// makes the first half of a rotation something that can be done without a restart.
/// </para>
/// <para>
/// Every failure here is a defect or a deployment that changed underneath a running process, never an operator error
/// waiting to be reported: startup already proved that the active key names a configured key and that every key's
/// material resolves and decodes. See
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0005-data-encryption-key-ring-and-provisioning.md">ADR 0005</see>.
/// </para>
/// </remarks>
internal sealed class DataEncryptionKeyRing
{
    private readonly Func<DataEncryptionKeyRingSettings> readSettings;
    private readonly ISecretReferenceResolver secretReferenceResolver;

    /// <summary>Initializes a key ring over the published settings and the secret resolver.</summary>
    /// <param name="readSettings">Reads the currently published key ring settings.</param>
    /// <param name="secretReferenceResolver">Turns a key's reference into material.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public DataEncryptionKeyRing(
        Func<DataEncryptionKeyRingSettings> readSettings,
        ISecretReferenceResolver secretReferenceResolver)
    {
        ArgumentNullException.ThrowIfNull(readSettings);
        ArgumentNullException.ThrowIfNull(secretReferenceResolver);

        this.readSettings = readSettings;
        this.secretReferenceResolver = secretReferenceResolver;
    }

    /// <summary>Gets whether the deployment configures any key material at all.</summary>
    /// <remarks>
    /// An absent ring is a supported deployment — ADR 0005 makes the section required by whatever first seals a value,
    /// not by starting — so a capability that needs key material asks this rather than failing when it finds none.
    /// </remarks>
    public bool IsConfigured => this.readSettings().Keys.Count > 0;

    /// <summary>Resolves the key new values are sealed under.</summary>
    /// <param name="cancellationToken">Cancels the resolution.</param>
    /// <returns>The active key, which the caller owns and must dispose.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the active key names no configured key, or its material no longer resolves or decodes.</exception>
    public Task<DataEncryptionKey> ResolveActiveKeyAsync(CancellationToken cancellationToken) =>
        this.ResolveKeyAsync(this.readSettings().ActiveKeyId, cancellationToken);

    /// <summary>Resolves the key a stored value names.</summary>
    /// <param name="keyId">The identifier stored beside the sealed value.</param>
    /// <param name="cancellationToken">Cancels the resolution.</param>
    /// <returns>The named key, which the caller owns and must dispose.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="keyId" /> is <see langword="null" />.</exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the ring configures no such key, or its material no longer resolves or decodes. A stored value naming
    /// a key the ring no longer holds is what retiring a key too early looks like, and it is deliberately a failure of
    /// the operation rather than something to work around: opening the value is impossible and pretending otherwise
    /// would replace a clear failure with a silent one.
    /// </exception>
    public async Task<DataEncryptionKey> ResolveKeyAsync(string keyId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(keyId);

        return await this.FindKeyAsync(keyId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"The data-encryption key ring configures no key '{keyId}'. A stored value names it, so the key was removed while values still referenced it.");
    }

    /// <summary>Resolves a key whose identifier came from somewhere this deployment does not vouch for.</summary>
    /// <param name="keyId">The identifier to look up, which may name nothing.</param>
    /// <param name="cancellationToken">Cancels the resolution.</param>
    /// <returns>The named key, which the caller owns and must dispose, or <see langword="null" /> when the ring configures no such key.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="keyId" /> is <see langword="null" />.</exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the ring does configure the key and its material no longer resolves or decodes, which is a
    /// deployment fault rather than something a caller supplied.
    /// </exception>
    /// <remarks>
    /// An unknown identifier is an ordinary answer here, unlike in <see cref="ResolveKeyAsync" />, because the caller is
    /// verifying something presented to it: a forged capability naming a key that never existed must be refused rather
    /// than raised, and the two are indistinguishable to whoever presented it either way.
    /// </remarks>
    public async Task<DataEncryptionKey?> FindKeyAsync(string keyId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(keyId);

        var settings = this.readSettings();

        var reference = settings.Keys.FirstOrDefault(key => string.Equals(key.KeyId, keyId, StringComparison.Ordinal));
        if (reference is null)
        {
            return null;
        }

        var resolution = await this.secretReferenceResolver.ResolveAsync(reference.Material.SecretReference, cancellationToken);

        using var material = resolution.Secret
            ?? throw new InvalidOperationException(
                $"The material of data-encryption key '{keyId}' could not be resolved [{resolution.Failure}].");

        return DataEncryptionKey.Decode(keyId, material, out var failure)
            ?? throw new InvalidOperationException(
                $"The material of data-encryption key '{keyId}' is not usable [{failure}].");
    }
}
