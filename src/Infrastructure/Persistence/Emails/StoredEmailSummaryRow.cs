// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Linq.Expressions;
using MailFathom.Application.Emails.Summaries;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Emails.Authentication;
using MailFathom.Domain.Folders;
using MailFathom.Infrastructure.Persistence.Entities;

namespace MailFathom.Infrastructure.Persistence.Emails;

/// <summary>The columns one email summary is built from, as PostgreSQL returns them.</summary>
/// <remarks>
/// <para>
/// The projection stops here rather than constructing the application read model directly, because a domain value
/// object's factory inside an <c>IQueryable</c> projection is either untranslatable or silently evaluated on the client.
/// Mapping outside the query keeps what PostgreSQL computes and what the process computes separable by reading.
/// </para>
/// <para>
/// It carries exactly the columns the summary carries and no others. Notably absent are the <c>Cc</c>, <c>Reply-To</c>,
/// and thread-reference arrays, which are filterable but not listed, and every column of the raw MIME table, which no
/// summary query joins to at all. The sender-authentication columns are the one group a listing reads without
/// publishing all of it: the single-email read is built from this same summary and publishes the evidence, and one
/// projection reading it is cheaper than a second query for the read that needs it.
/// </para>
/// <para>
/// The projection and the mapping live on the row rather than in either reader, because a listing and a single-email
/// lookup publish the same summary. Two copies of a twenty-column projection would drift, and the column a copy forgot
/// would be missing from one of the two paths only.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed record StoredEmailSummaryRow(
    Guid Id,
    string MailboxAccountId,
    string FolderAlias,
    string? InternetMessageId,
    string? Subject,
    DateTimeOffset? SentAt,
    DateTimeOffset? ReceivedAt,
    long SizeOctets,
    string? SenderDisplayName,
    string? SenderAddress,
    string[] ToAddresses,
    int AttachmentCount,
    long AttachmentTotalSizeOctets,
    int InlineResourceCount,
    bool IsEncrypted,
    bool CarriesUnverifiedSignature,
    bool ContainsUnexpandedTnefPart,
    StoredEmailContentAvailability ContentAvailability,
    DateTimeOffset? RemoteFlagsObservedAt,
    bool IsRemotelySeen,
    bool IsRemotelyAnswered,
    bool IsRemotelyFlagged,
    bool IsRemotelyDraft,
    bool IsRemotelyDeleted,
    string[] RemoteKeywords,
    AuthorAuthenticationOutcome AuthorAuthenticationOutcome,
    SenderTrustLevel SenderTrustLevel,
    string? AuthenticatedSenderDomain,
    string? DisplayedAuthorDomain,
    SenderAuthenticationMethod SenderAuthenticationMethod,
    DmarcOutcome DmarcOutcome)
{
    /// <summary>Gets the projection every summary query selects, which is what keeps the two readers publishing one shape.</summary>
    public static Expression<Func<StoredEmailEntity, StoredEmailSummaryRow>> Projection { get; } = email =>
        new StoredEmailSummaryRow(
            email.Id,
            email.MailboxAccountId,
            email.MailFolder.Alias,
            email.InternetMessageId,
            email.Subject,
            email.SentAt,
            email.ReceivedAt,
            email.SizeOctets,
            email.SenderDisplayName,
            email.SenderAddress,
            email.ToAddresses,
            email.AttachmentCount,
            email.AttachmentTotalSizeOctets,
            email.InlineResourceCount,
            email.IsEncrypted,
            email.CarriesUnverifiedSignature,
            email.ContainsUnexpandedTnefPart,
            email.ContentAvailability,
            email.RemoteFlagsObservedAt,
            email.IsRemotelySeen,
            email.IsRemotelyAnswered,
            email.IsRemotelyFlagged,
            email.IsRemotelyDraft,
            email.IsRemotelyDeleted,
            email.RemoteKeywords,
            email.AuthorAuthenticationOutcome,
            email.SenderTrustLevel,
            email.AuthenticatedSenderDomain,
            email.DisplayedAuthorDomain,
            email.SenderAuthenticationMethod,
            email.DmarcOutcome);

    /// <summary>Turns the returned columns into the application read model.</summary>
    /// <returns>The summary, with every domain value object built by its own factory.</returns>
    public EmailSummary ToSummary() => new()
    {
        StoredEmailId = StoredEmailId.Create(this.Id),
        AccountId = MailAccountId.Create(this.MailboxAccountId),
        FolderAlias = MailFolderAlias.Create(this.FolderAlias),
        InternetMessageId = this.InternetMessageId,
        Subject = this.Subject,
        SentAt = this.SentAt,
        ReceivedAt = this.ReceivedAt,
        SizeOctets = this.SizeOctets,
        SenderDisplayName = this.SenderDisplayName,
        SenderAddress = this.SenderAddress,
        // A read-only view rather than the array itself, which a caller could cast back and write through.
        ToAddresses = Array.AsReadOnly(this.ToAddresses),
        Attachments = new StoredEmailAttachmentSummary(
            this.AttachmentCount,
            this.AttachmentTotalSizeOctets,
            this.InlineResourceCount,
            this.IsEncrypted,
            this.CarriesUnverifiedSignature,
            this.ContainsUnexpandedTnefPart),
        ContentAvailability = this.ContentAvailability,
        RemoteFlags = new RemoteEmailFlagSnapshot(
            this.RemoteFlagsObservedAt,
            this.IsRemotelySeen,
            this.IsRemotelyAnswered,
            this.IsRemotelyFlagged,
            this.IsRemotelyDraft,
            this.IsRemotelyDeleted,
            // Rebuilt through the factory rather than wrapped, because the column is what an earlier build's
            // normalization left behind and this one's rules are the ones a caller's filter is folded by.
            RemoteEmailKeywords.Create(this.RemoteKeywords)),
        SenderVerification = new SenderVerification
        {
            AuthorAuthentication = this.AuthorAuthenticationOutcome,
            DeploymentTrust = this.SenderTrustLevel,
        },
        SenderAuthenticationEvidence = new SenderAuthenticationEvidence
        {
            AuthenticatedDomain = StoredDomain(this.AuthenticatedSenderDomain),
            DisplayedAuthorDomain = StoredDomain(this.DisplayedAuthorDomain),
            AuthenticatedBy = this.SenderAuthenticationMethod,
            Dmarc = this.DmarcOutcome,
        },
    };

    /// <summary>Reads one stored domain column back into its value object, or reports that the column holds none.</summary>
    /// <remarks>
    /// A column value the factory refuses is read as absent rather than raised, because the alternative is a stored
    /// email that can never be listed again. Nothing writes one: the columns are written from the same value object,
    /// and its own bound is what the column length was taken from.
    /// </remarks>
    private static SenderDomain? StoredDomain(string? domain) =>
        domain is not null && SenderDomain.TryCreate(domain, out var stored) ? stored : null;
}
