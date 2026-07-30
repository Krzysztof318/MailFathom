// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.CodeCoverage;
using MailMcp.Domain.Emails;

namespace MailMcp.Infrastructure.Persistence;

/// <summary>The columns one row of a mailbox listing is built from, as PostgreSQL returns them.</summary>
/// <remarks>
/// <para>
/// The projection stops here rather than constructing the application read model directly, because a domain value
/// object's factory inside an <c>IQueryable</c> projection is either untranslatable or silently evaluated on the client.
/// Mapping outside the query keeps what PostgreSQL computes and what the process computes separable by reading.
/// </para>
/// <para>
/// It carries exactly the columns the summary publishes and no others. Notably absent are the <c>Cc</c>, <c>Reply-To</c>,
/// and thread-reference arrays, which are filterable but not listed, and every column of the raw MIME table, which no
/// listing query joins to at all.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed record StoredEmailTimelineRow(
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
    bool IsRemotelyDeleted);
