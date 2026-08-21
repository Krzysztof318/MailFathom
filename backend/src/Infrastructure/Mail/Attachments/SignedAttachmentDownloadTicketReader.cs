// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Security.Cryptography;
using MailFathom.Application.EmailContent.Attachments;
using MailFathom.Domain.Emails;
using MailFathom.Infrastructure.DataEncryption;

namespace MailFathom.Infrastructure.Mail.Attachments;

/// <summary>Verifies a presented attachment capability against the deployment's key ring.</summary>
/// <remarks>
/// <para>
/// Everything this reads arrives from a request nobody has authenticated, so the order is the rule: the shape is
/// checked before anything is indexed, the named key is looked up without being trusted, the tag is compared in fixed
/// time, and only a capability that has survived all three is judged against the clock. The key identifier is read
/// first out of necessity — the signature cannot be checked without the key that produced it — and looking a
/// configured key up is the only thing it is used for.
/// </para>
/// <para>
/// Every refusal is the same refusal, and none of them is logged with the capability that produced it: the presented
/// text is an unauthenticated way to obtain mail content, so a log line carrying one would be worse than the failure it
/// records.
/// </para>
/// </remarks>
internal sealed class SignedAttachmentDownloadTicketReader : IAttachmentDownloadTicketReader
{
    private readonly DataEncryptionKeyRing keyRing;
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes a reader over the deployment's key ring.</summary>
    /// <param name="keyRing">Resolves the key a presented capability names.</param>
    /// <param name="timeProvider">Decides whether a capability has expired.</param>
    /// <exception cref="ArgumentNullException">Thrown when either argument is <see langword="null" />.</exception>
    public SignedAttachmentDownloadTicketReader(DataEncryptionKeyRing keyRing, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(keyRing);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.keyRing = keyRing;
        this.timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public async Task<AttachmentDownloadTicket?> RedeemAsync(string capability, CancellationToken cancellationToken)
    {
        if (!AttachmentDownloadCapability.TryReadKeyId(capability, out var keyId) || keyId is null)
        {
            return null;
        }

        using var key = await this.keyRing.FindKeyAsync(keyId, cancellationToken);
        if (key is null)
        {
            return null;
        }

        var signingKey = GC.AllocateArray<byte>(AttachmentDownloadCapability.SigningKeySizeInBytes, pinned: true);

        try
        {
            key.DeriveKeyFor(DataEncryptionPurpose.AttachmentDownloadLink, signingKey);

            return AttachmentDownloadCapability.TryVerify(
                capability,
                signingKey,
                this.timeProvider.GetUtcNow(),
                out var storedEmailId,
                out var attachmentPosition)
                ? new AttachmentDownloadTicket(StoredEmailId.Create(storedEmailId), attachmentPosition)
                : null;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(signingKey);
        }
    }
}
