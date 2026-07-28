// Copyright © 2026 Krzysztof Kasprowicz

using System.Linq.Expressions;
using MailMcp.Application.Emails;
using MailMcp.CodeCoverage;
using MailMcp.Domain.Emails;
using Microsoft.EntityFrameworkCore;

namespace MailMcp.Infrastructure.Persistence;

/// <summary>Writes the derived search document of one stored email inside the caller's open session.</summary>
/// <remarks>
/// Both writers of extracted metadata use this: synchronization, which has just read a message it fetched, and the
/// backfill, which re-derives from raw MIME stored before extraction existed. Keeping one writer is what stops the two
/// paths from producing documents built to different rules.
/// </remarks>
[RequiresIntegrationCoverage]
internal static class EmailSearchDocumentWriter
{
    /// <summary>Saves the search document derived from one extraction, replacing any earlier one.</summary>
    /// <param name="dbContext">The context whose transaction this write joins.</param>
    /// <param name="storedEmail">The email the document belongs to, tracked or already persisted.</param>
    /// <param name="metadata">What the MIME reader extracted.</param>
    /// <param name="extractedAt">When the extraction ran.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>A task that completes when the write has been issued or staged.</returns>
    /// <exception cref="ArgumentNullException">Thrown when any reference argument is <see langword="null" />.</exception>
    public static async Task SaveAsync(
        MailMcpDbContext dbContext,
        StoredEmailEntity storedEmail,
        ExtractedEmailMetadata metadata,
        DateTimeOffset extractedAt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(storedEmail);
        ArgumentNullException.ThrowIfNull(metadata);

        var subjectText = IndexedSubject(metadata.Subject);
        var participantAddresses = IndexedParticipantAddresses(metadata.Participants);
        var text = metadata.Text;

        // The change-tracker pass comes first for the reason the content store's does: a document staged earlier in
        // this same uncommitted session is not visible to a set-based update, and updating it twice would insert twice.
        Expression<Func<EmailSearchDocumentEntity, bool>> matchesStoredEmail =
            candidate => candidate.StoredEmailId == storedEmail.Id;
        var trackedDocument = dbContext.EmailSearchDocuments.Local.AsQueryable().SingleOrDefault(matchesStoredEmail);
        if (trackedDocument is not null)
        {
            trackedDocument.SubjectText = subjectText;
            trackedDocument.ParticipantAddresses = participantAddresses;
            trackedDocument.BodyText = text.TrimmedText;
            trackedDocument.BodyTextBeforeTrimming = text.OriginalText;
            trackedDocument.TextSource = text.Source;
            trackedDocument.ExtractedAt = extractedAt;

            return;
        }

        // Re-deriving text for an email that already has a document must not read its existing body text back into
        // memory or into the change tracker, so the overwrite is a set-based update inside the caller's transaction.
        var updatedRowCount = await dbContext.EmailSearchDocuments
            .Where(matchesStoredEmail)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(candidate => candidate.SubjectText, subjectText)
                    .SetProperty(candidate => candidate.ParticipantAddresses, participantAddresses)
                    .SetProperty(candidate => candidate.BodyText, text.TrimmedText)
                    .SetProperty(candidate => candidate.BodyTextBeforeTrimming, text.OriginalText)
                    .SetProperty(candidate => candidate.TextSource, text.Source)
                    .SetProperty(candidate => candidate.ExtractedAt, extractedAt),
                cancellationToken);

        if (updatedRowCount == 0)
        {
            dbContext.EmailSearchDocuments.Add(new EmailSearchDocumentEntity
            {
                StoredEmailId = storedEmail.Id,
                StoredEmail = storedEmail,
                SubjectText = subjectText,
                ParticipantAddresses = participantAddresses,
                BodyText = text.TrimmedText,
                BodyTextBeforeTrimming = text.OriginalText,
                TextSource = text.Source,
                ExtractedAt = extractedAt,
            });
        }
    }

    /// <summary>Bounds the subject copy the index covers, keeping the stored email's own subject untouched.</summary>
    private static string? IndexedSubject(string? subject) => subject is null
        ? null
        : MailTextBounds.TruncateAtTextElementBoundary(subject, EmailSearchDocumentEntity.MaximumIndexedSubjectLength);

    /// <summary>Builds the one text value the index reads every participant address from.</summary>
    /// <remarks>
    /// Only the comparison form is indexed, so a query matches an address however the sender capitalized it, and the
    /// display names stay out of a second searchable copy of who somebody corresponds with. Roles are not separated,
    /// because the index answers "this address appears on this message" and the stored email's own columns are what a
    /// role-specific filter reads.
    /// </remarks>
    private static string? IndexedParticipantAddresses(IReadOnlyList<EmailParticipant> participants)
    {
        var addresses = participants
            .Select(participant => participant.Address.NormalizedAddress)
            .Where(address => address.Length <= StoredEmailEntity.MaximumAddressLength)
            .Distinct(StringComparer.Ordinal)
            .Take(EmailSearchDocumentEntity.MaximumIndexedParticipantAddresses)
            .ToArray();

        return addresses.Length == 0 ? null : string.Join(' ', addresses);
    }
}
