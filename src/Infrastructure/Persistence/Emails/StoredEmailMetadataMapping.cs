// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Extraction;
using MailFathom.Application.Emails.Summaries;
using MailFathom.Application.Synchronization;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Emails.Authentication;
using MailFathom.Infrastructure.Persistence.Entities;

namespace MailFathom.Infrastructure.Persistence.Emails;

/// <summary>Writes what synchronization learned about one message onto its persistence row.</summary>
/// <remarks>
/// The two sources are applied in order and deliberately not merged field by field. The remote summary comes from the
/// server's own parse of the message and is all an oversized occurrence ever gets; the extracted metadata comes from
/// the raw MIME this deployment actually stored, which is the same payload every later reader re-derives from. Letting
/// extraction overwrite the summary wholesale keeps one row consistent with one set of bytes, instead of a row whose
/// subject came from one parser and whose date came from another.
/// </remarks>
internal static class StoredEmailMetadataMapping
{
    /// <summary>Writes the summary the mail server reported for one occurrence.</summary>
    /// <param name="entity">The row to write onto.</param>
    /// <param name="metadata">What the server's envelope reported.</param>
    /// <param name="contentAvailability">Whether raw MIME is stored for this occurrence, or why it is not.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="entity" /> or <paramref name="metadata" /> is <see langword="null" />.</exception>
    public static void ApplyRemoteSummary(
        StoredEmailEntity entity,
        RemoteEmailMetadata metadata,
        StoredEmailContentAvailability contentAvailability)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(metadata);

        entity.InternetMessageId = WithinIdentifierBound(metadata.InternetMessageId);
        entity.Subject = metadata.Subject;
        entity.SentAt = metadata.SentAt;
        entity.SizeOctets = metadata.SizeOctets;
        entity.ContentAvailability = contentAvailability;
    }

    /// <summary>Writes the normalized metadata read out of the stored raw MIME.</summary>
    /// <param name="entity">The row to write onto.</param>
    /// <param name="metadata">What the MIME reader extracted.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="entity" /> or <paramref name="metadata" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// A message that carried no <c>Message-ID</c> keeps the identifier the envelope reported rather than losing it,
    /// because the two agree whenever both are present and the envelope is the only source when a header is folded in
    /// a way this reader refused. Every other extracted field replaces what the envelope said.
    /// </remarks>
    public static void ApplyExtractedMetadata(StoredEmailEntity entity, ExtractedEmailMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(metadata);

        entity.Subject = metadata.Subject;
        entity.SentAt = metadata.SentAt;
        entity.ReceivedAt = metadata.ReceivedAt;

        ApplySender(entity, metadata.Participants);
        entity.ToAddresses = NormalizedAddressesInRole(metadata.Participants, EmailAddressRole.To);
        entity.CcAddresses = NormalizedAddressesInRole(metadata.Participants, EmailAddressRole.Cc);
        entity.ReplyToAddresses = NormalizedAddressesInRole(metadata.Participants, EmailAddressRole.ReplyTo);

        entity.InternetMessageId =
            WithinIdentifierBound(metadata.ThreadReferences.MessageId) ?? entity.InternetMessageId;
        entity.InReplyTo = WithinIdentifierBound(metadata.ThreadReferences.InReplyTo);
        entity.ThreadReferences = BoundedIdentifiers(metadata.ThreadReferences.References);

        ApplyAttachmentSummary(entity, metadata.Attachments);
        ApplySenderAuthentication(entity, metadata.SenderAuthentication);
        ApplySenderTrust(entity, metadata.SenderTrust);
    }

    /// <summary>Records what the receiving mail server established about who sent the message.</summary>
    /// <remarks>
    /// Written whole on every extraction, including the not-established verdict, so a re-derivation after an account's
    /// trusted authority changed replaces the whole group rather than leaving one column from the previous reading. The
    /// domains need no length guard of their own: the domain value refuses anything longer than the column accepts.
    /// </remarks>
    private static void ApplySenderAuthentication(StoredEmailEntity entity, SenderAuthentication authentication)
    {
        entity.SenderAuthenticationOutcome = authentication.Outcome;
        entity.SenderAuthenticationMethod = authentication.AuthenticatedBy;
        entity.AuthenticatedSenderDomain = authentication.AuthenticatedDomain?.NormalizedValue;
        entity.DkimSignerDomain = authentication.DkimDomain?.NormalizedValue;
        entity.SpfMailFromDomain = authentication.SpfDomain?.NormalizedValue;
        entity.DisplayedAuthorDomain = authentication.FromDomain?.NormalizedValue;
        entity.DmarcOutcome = authentication.Dmarc;
        entity.AuthorAuthenticationOutcome = authentication.AuthorAuthentication;
        entity.AuthenticatedAuthorDomain = authentication.AuthenticatedAuthorDomain?.NormalizedValue;
    }

    /// <summary>Records what this deployment made of the author the message authenticated as.</summary>
    /// <remarks>
    /// Written whole beside the verdict it reads, so a re-derivation after a trusted-sender list changed replaces both
    /// halves rather than leaving an answer beside the identity a different list judged. A revision that names no
    /// policy is stored as absent rather than as an empty string, because the column's null is what says no policy
    /// judged this row.
    /// </remarks>
    private static void ApplySenderTrust(StoredEmailEntity entity, SenderTrust trust)
    {
        entity.SenderTrustLevel = trust.Level;
        entity.SenderTrustGrantedBy = trust.GrantedBy;
        entity.SenderTrustPolicyRevision = trust.PolicyRevision.NamesAPolicy
            ? trust.PolicyRevision.Value
            : null;
    }

    /// <summary>Records the one participant a timeline names as the message's sender.</summary>
    /// <remarks>
    /// <c>From</c> names the author and is what a reader means by the sender. <c>Sender</c> is the fallback rather than
    /// the first choice: it names whoever submitted a message written on someone else's behalf, so it answers a
    /// different question and only stands in for a message that wrote no author at all. The first address wins when a
    /// header carried several, because the column names one sender and the full list stays in the raw MIME.
    /// </remarks>
    private static void ApplySender(StoredEmailEntity entity, IReadOnlyList<EmailParticipant> participants)
    {
        var sender = FirstStorableParticipantInRole(participants, EmailAddressRole.From)
            ?? FirstStorableParticipantInRole(participants, EmailAddressRole.Sender);

        entity.SenderDisplayName = sender?.Address.DisplayName;
        entity.SenderAddress = sender?.Address.Address;
        entity.SenderNormalizedAddress = sender?.Address.NormalizedAddress;
    }

    private static void ApplyAttachmentSummary(StoredEmailEntity entity, EmailAttachmentSummary attachments)
    {
        entity.AttachmentCount = attachments.AttachmentCount;
        entity.AttachmentTotalSizeOctets = attachments.TotalSizeOctets;
        entity.InlineResourceCount = attachments.InlineResourceCount;
        entity.IsEncrypted = attachments.IsEncrypted;
        entity.CarriesUnverifiedSignature = attachments.CarriesUnverifiedSignature;
        entity.ContainsUnexpandedTnefPart = attachments.ContainsUnexpandedTnefPart;
    }

    private static EmailParticipant? FirstStorableParticipantInRole(
        IReadOnlyList<EmailParticipant> participants,
        EmailAddressRole role) =>
        participants.FirstOrDefault(participant =>
            participant.Role == role && WithinAddressBound(participant.Address.Address));

    /// <summary>Collects the comparison forms one header contributed, in header order and without repeats.</summary>
    private static string[] NormalizedAddressesInRole(IReadOnlyList<EmailParticipant> participants, EmailAddressRole role) =>
    [
        .. participants
            .Where(participant => participant.Role == role)
            .Select(participant => participant.Address.NormalizedAddress)
            .Where(WithinAddressBound)
            .Distinct(StringComparer.Ordinal)
            .Take(StoredEmailEntity.MaximumAddressesPerRole),
    ];

    /// <summary>Keeps the ancestors nearest to this message, which is the end of the path a thread view walks first.</summary>
    private static string[] BoundedIdentifiers(IReadOnlyList<string> identifiers) =>
    [
        .. identifiers
            .Reverse()
            .Where(IsWithinIdentifierBound)
            .Take(StoredEmailEntity.MaximumThreadReferences)
            .Reverse(),
    ];

    /// <summary>Refuses an identifier too long for its column instead of storing a prefix of one.</summary>
    /// <remarks>
    /// Nothing between the mail server and this row bounds a header's length, so the bound is applied here rather than
    /// left to PostgreSQL. A rejected write would be worse than a dropped value: the commit fails, the retry budget
    /// runs out, the folder checkpoint never advances past the message, and every later run stops on the same one.
    /// Truncating would be worse still, because a prefix of a message identifier is an identifier some other message
    /// may legitimately carry, and a thread would be assembled from it.
    /// </remarks>
    private static string? WithinIdentifierBound(string? identifier) =>
        identifier is not null && IsWithinIdentifierBound(identifier) ? identifier : null;

    private static bool IsWithinIdentifierBound(string identifier) =>
        identifier.Length <= StoredEmailEntity.MaximumIdentifierLength;

    private static bool WithinAddressBound(string address) =>
        address.Length <= StoredEmailEntity.MaximumAddressLength;
}
