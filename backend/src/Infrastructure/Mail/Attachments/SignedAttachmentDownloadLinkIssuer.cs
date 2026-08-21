// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Security.Cryptography;
using MailFathom.Application.EmailContent.Attachments;
using MailFathom.Domain.Emails;
using MailFathom.Infrastructure.DataEncryption;

namespace MailFathom.Infrastructure.Mail.Attachments;

/// <summary>Mints attachment download links, signed with a key derived from the deployment's own key ring.</summary>
/// <remarks>
/// <para>
/// The signing material is the ring's rather than a secret of its own, so a deployment that already provisions data
/// encryption provisions nothing further to hand out links, and the ring's rotation applies unchanged: a link minted
/// under the previous active key stays verifiable for its own lifetime because it names the key it was signed with.
/// A deployment configuring no ring mints no link, which is a configuration state rather than a failure.
/// </para>
/// <para>
/// The key is resolved once per email and erased before the method returns, which is the rule every other consumer of
/// the ring follows. The derived subkey is erased with it, and neither it nor a composed link is ever logged.
/// </para>
/// </remarks>
internal sealed class SignedAttachmentDownloadLinkIssuer : IAttachmentDownloadLinkIssuer
{
    private readonly DataEncryptionKeyRing keyRing;
    private readonly AttachmentDownloadSettings settings;
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes an issuer over the deployment's key ring and declared address.</summary>
    /// <param name="keyRing">Resolves the key the signing key is derived from.</param>
    /// <param name="settings">Where links point and how long they live.</param>
    /// <param name="timeProvider">Decides when a minted link expires.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public SignedAttachmentDownloadLinkIssuer(
        DataEncryptionKeyRing keyRing,
        AttachmentDownloadSettings settings,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(keyRing);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.keyRing = keyRing;
        this.settings = settings;
        this.timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public bool CanIssueLinks => this.settings.DownloadAddressPrefix is not null && this.keyRing.IsConfigured;

    /// <inheritdoc />
    public async Task<IReadOnlyList<AttachmentDownloadLink>> IssueAsync(
        StoredEmailId storedEmailId,
        int attachmentCount,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(attachmentCount);

        if (this.settings.DownloadAddressPrefix is not { } addressPrefix
            || !this.keyRing.IsConfigured
            || attachmentCount == 0)
        {
            return [];
        }

        var expiresAt = this.timeProvider.GetUtcNow() + this.settings.LinkLifetime;

        using var key = await this.keyRing.ResolveActiveKeyAsync(cancellationToken);

        // Pinned for the reason every other buffer holding key material in this system is: an unpinned buffer can be
        // relocated by the collector while it holds live material, leaving a copy behind that the erasure never reaches.
        var signingKey = GC.AllocateArray<byte>(AttachmentDownloadCapability.SigningKeySizeInBytes, pinned: true);

        try
        {
            key.DeriveKeyFor(DataEncryptionPurpose.AttachmentDownloadLink, signingKey);

            return Compose(addressPrefix, key.KeyId, storedEmailId, attachmentCount, expiresAt, signingKey);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(signingKey);
        }
    }

    /// <summary>Composes one link per attachment, all expiring together.</summary>
    /// <remarks>
    /// One expiry for the whole message rather than one per link, because they are handed over in one response: a
    /// caller that fetches the third file last would otherwise find it dead first for no reason it could see.
    /// </remarks>
    private static AttachmentDownloadLink[] Compose(
        Uri addressPrefix,
        string keyId,
        StoredEmailId storedEmailId,
        int attachmentCount,
        DateTimeOffset expiresAt,
        ReadOnlySpan<byte> signingKey)
    {
        var links = new AttachmentDownloadLink[attachmentCount];

        for (var position = 0; position < attachmentCount; position++)
        {
            var capability = AttachmentDownloadCapability.Compose(
                keyId,
                storedEmailId.Value,
                position,
                expiresAt,
                signingKey);

            links[position] = new AttachmentDownloadLink(new Uri(addressPrefix, capability), expiresAt);
        }

        return links;
    }
}
