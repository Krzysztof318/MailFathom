// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Secrets.Resolution;

namespace MailFathom.Infrastructure.ObjectStorage;

/// <summary>The access key one request to the object-storage endpoint is signed with, already resolved from the references that declared it.</summary>
/// <remarks>
/// <para>
/// The instance is owned by the operation that resolved it and released when that operation ends, which bounds the
/// window in which a process dump could hold the access key to one call rather than to process uptime. A key rotated
/// behind an unchanged reference is therefore picked up by the next operation, with no cache to invalidate.
/// </para>
/// <para>
/// Both halves are secret-bearing, which is why the identifier is resolved the same way the secret is rather than
/// written into an appsettings file beside it: an access key identifier names an identity at the endpoint, it is one
/// half of what an attacker needs, and every provider that issues one issues it together with its secret from the same
/// place. This type owns both buffers and erases them together, so neither can be released while the other is still
/// held.
/// </para>
/// </remarks>
public sealed class ObjectStorageCredential : IDisposable
{
    private readonly ResolvedSecret accessKeyIdMaterial;
    private readonly ResolvedSecret secretAccessKeyMaterial;

    private ObjectStorageCredential(ResolvedSecret accessKeyIdMaterial, ResolvedSecret secretAccessKeyMaterial)
    {
        this.accessKeyIdMaterial = accessKeyIdMaterial;
        this.secretAccessKeyMaterial = secretAccessKeyMaterial;

        // Revealed once, here, rather than on every read: the signer takes strings, so the material is already in
        // managed memory the moment it is presented, and revealing it repeatedly would only multiply the copies.
        this.AccessKeyId = accessKeyIdMaterial.RevealAsString();
        this.SecretAccessKey = secretAccessKeyMaterial.RevealAsString();
    }

    /// <summary>Gets the resolved access key identifier the request is signed as.</summary>
    public string AccessKeyId { get; }

    /// <summary>Gets the resolved secret the request's signature is derived from.</summary>
    public string SecretAccessKey { get; }

    /// <summary>Builds a credential from resolved material, taking ownership of both buffers.</summary>
    /// <param name="accessKeyIdMaterial">The material the access key identifier was read from.</param>
    /// <param name="secretAccessKeyMaterial">The material the secret was read from.</param>
    /// <returns>The credential.</returns>
    /// <exception cref="ArgumentNullException">Thrown when either argument is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when either half is blank, which is a credential the endpoint could not admit and must never be sent as one.</exception>
    /// <remarks>Both buffers are released when this credential is, including when a blank half refuses the construction.</remarks>
    public static ObjectStorageCredential Create(
        ResolvedSecret accessKeyIdMaterial,
        ResolvedSecret secretAccessKeyMaterial)
    {
        ArgumentNullException.ThrowIfNull(accessKeyIdMaterial);
        ArgumentNullException.ThrowIfNull(secretAccessKeyMaterial);

        var credential = new ObjectStorageCredential(accessKeyIdMaterial, secretAccessKeyMaterial);

        try
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(credential.AccessKeyId, nameof(accessKeyIdMaterial));
            ArgumentException.ThrowIfNullOrWhiteSpace(credential.SecretAccessKey, nameof(secretAccessKeyMaterial));

            return credential;
        }
        catch
        {
            credential.Dispose();

            throw;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        this.accessKeyIdMaterial.Dispose();
        this.secretAccessKeyMaterial.Dispose();
    }
}
