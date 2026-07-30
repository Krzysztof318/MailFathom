// Copyright © 2026 Krzysztof Kasprowicz

using System.Linq.Expressions;
using MailMcp.Application.Emails;
using MailMcp.CodeCoverage;
using MailMcp.Domain.Accounts;
using MailMcp.Domain.Emails;
using MailMcp.Domain.Folders;

namespace MailMcp.Infrastructure.Persistence;

/// <summary>The one query projection every read model publishes an email summary through.</summary>
/// <remarks>
/// <para>
/// The projection is the privacy control before it is a performance one: it names the columns a summary publishes, so
/// no read path can reach the stored raw MIME or the derived body text even by accident. Written once, it is also
/// auditable once — a second copy would have to be found and read before anyone could say what a mailbox read is
/// capable of returning.
/// </para>
/// <para>
/// It stops at the row rather than constructing the application read model directly, because a domain value object's
/// factory inside an <c>IQueryable</c> projection is either untranslatable or silently evaluated on the client. Mapping
/// outside the query keeps what PostgreSQL computes and what the process computes separable by reading.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal static class StoredEmailSummaryProjection
{
    /// <summary>Gets the projection from a stored email onto the columns a summary is built from.</summary>
    internal static Expression<Func<StoredEmailEntity, StoredEmailTimelineRow>> Row { get; } =
        email => new StoredEmailTimelineRow(
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
            email.IsRemotelyDeleted);

    /// <summary>Maps one returned row onto the application read model.</summary>
    /// <param name="row">The columns PostgreSQL returned.</param>
    /// <returns>The summary a read model publishes.</returns>
    internal static EmailSummary ToSummary(StoredEmailTimelineRow row) => new()
    {
        StoredEmailId = StoredEmailId.Create(row.Id),
        AccountId = MailAccountId.Create(row.MailboxAccountId),
        FolderAlias = MailFolderAlias.Create(row.FolderAlias),
        InternetMessageId = row.InternetMessageId,
        Subject = row.Subject,
        SentAt = row.SentAt,
        ReceivedAt = row.ReceivedAt,
        SizeOctets = row.SizeOctets,
        SenderDisplayName = row.SenderDisplayName,
        SenderAddress = row.SenderAddress,
        // A read-only view rather than the array itself, which a caller could cast back and write through.
        ToAddresses = Array.AsReadOnly(row.ToAddresses),
        Attachments = new StoredEmailAttachmentSummary(
            row.AttachmentCount,
            row.AttachmentTotalSizeOctets,
            row.InlineResourceCount,
            row.IsEncrypted,
            row.CarriesUnverifiedSignature,
            row.ContainsUnexpandedTnefPart),
        ContentAvailability = row.ContentAvailability,
        RemoteFlags = new RemoteEmailFlagSnapshot(
            row.RemoteFlagsObservedAt,
            row.IsRemotelySeen,
            row.IsRemotelyAnswered,
            row.IsRemotelyFlagged,
            row.IsRemotelyDraft,
            row.IsRemotelyDeleted),
    };
}
