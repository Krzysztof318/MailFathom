// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using System.Linq.Expressions;
using MailFathom.Application.Emails;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Emails;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence;

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
        MailFathomDbContext dbContext,
        StoredEmailEntity storedEmail,
        ExtractedEmailMetadata metadata,
        DateTimeOffset extractedAt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(storedEmail);
        ArgumentNullException.ThrowIfNull(metadata);

        var text = metadata.Text;
        var document = new DerivedDocument(
            IndexedSubject(metadata.Subject),
            IndexedParticipantAddresses(metadata.Participants),
            text.TrimmedText,
            text.OriginalText,
            text.Source,
            extractedAt);

        // The change-tracker pass comes first for the reason the content store's does: a document staged earlier in
        // this same uncommitted session is not visible to a set-based update, and updating it twice would insert twice.
        var trackedDocument = FindTracked(dbContext, storedEmail.Id);
        if (trackedDocument is not null)
        {
            Apply(trackedDocument, document);

            return;
        }

        // Re-deriving text for an email that already has a document must not read its existing body text back into
        // memory or into the change tracker, so the overwrite is a set-based update inside the caller's transaction.
        var updatedRowCount = await dbContext.EmailSearchDocuments
            .Where(MatchesStoredEmail(storedEmail.Id))
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(candidate => candidate.SubjectText, document.SubjectText)
                    .SetProperty(candidate => candidate.ParticipantAddresses, document.ParticipantAddresses)
                    .SetProperty(candidate => candidate.BodyText, document.BodyText)
                    .SetProperty(candidate => candidate.BodyTextBeforeTrimming, document.BodyTextBeforeTrimming)
                    .SetProperty(candidate => candidate.TextSource, document.TextSource)
                    .SetProperty(candidate => candidate.ExtractedAt, document.ExtractedAt),
                cancellationToken);

        if (updatedRowCount == 0)
        {
            Insert(dbContext, storedEmail, document);
        }
    }

    /// <summary>Saves a document built from the server's envelope alone, for a message whose body was never read.</summary>
    /// <param name="dbContext">The context whose transaction this write joins.</param>
    /// <param name="storedEmail">The email the document belongs to, tracked or already persisted.</param>
    /// <param name="subject">The subject the server's envelope reported.</param>
    /// <param name="recordedAt">When the occurrence was recorded.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>A task that completes when the write has been issued or staged.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="dbContext" /> or <paramref name="storedEmail" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// <para>
    /// A message that exceeded the size limit, and one whose stored MIME no reader could parse, would otherwise carry
    /// no search vector at all and be findable by nothing — not even the subject the server did report. Indexing what
    /// is known keeps the gap to the body it actually concerns.
    /// </para>
    /// <para>
    /// This never overwrites an existing document, which is the same rule the metadata mapping follows for the fields
    /// only extraction supplies. The remote message is immutable, so a run that could not read it this time is no
    /// reason to forget the body a run that could read it wrote earlier.
    /// </para>
    /// </remarks>
    public static async Task SaveEnvelopeOnlyAsync(
        MailFathomDbContext dbContext,
        StoredEmailEntity storedEmail,
        string? subject,
        DateTimeOffset recordedAt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(storedEmail);

        if (FindTracked(dbContext, storedEmail.Id) is not null)
        {
            return;
        }

        var isAlreadyStored = await dbContext.EmailSearchDocuments
            .AnyAsync(MatchesStoredEmail(storedEmail.Id), cancellationToken);
        if (isAlreadyStored)
        {
            return;
        }

        Insert(
            dbContext,
            storedEmail,
            new DerivedDocument(
                IndexedSubject(subject),
                ParticipantAddresses: null,
                BodyText: null,
                BodyTextBeforeTrimming: null,
                ExtractedEmailTextSource.BodyNotExtracted,
                recordedAt));
    }

    private static Expression<Func<EmailSearchDocumentEntity, bool>> MatchesStoredEmail(Guid storedEmailId) =>
        candidate => candidate.StoredEmailId == storedEmailId;

    private static EmailSearchDocumentEntity? FindTracked(MailFathomDbContext dbContext, Guid storedEmailId) =>
        dbContext.EmailSearchDocuments.Local.AsQueryable().SingleOrDefault(MatchesStoredEmail(storedEmailId));

    private static void Insert(
        MailFathomDbContext dbContext,
        StoredEmailEntity storedEmail,
        DerivedDocument document)
    {
        var entity = new EmailSearchDocumentEntity
        {
            StoredEmailId = storedEmail.Id,
            StoredEmail = storedEmail,
        };

        Apply(entity, document);
        dbContext.EmailSearchDocuments.Add(entity);
    }

    private static void Apply(EmailSearchDocumentEntity entity, DerivedDocument document)
    {
        entity.SubjectText = document.SubjectText;
        entity.ParticipantAddresses = document.ParticipantAddresses;
        entity.BodyText = document.BodyText;
        entity.BodyTextBeforeTrimming = document.BodyTextBeforeTrimming;
        entity.TextSource = document.TextSource;
        entity.ExtractedAt = document.ExtractedAt;
    }

    /// <summary>Bounds the subject copy the index covers, keeping the stored email's own subject untouched.</summary>
    /// <remarks>
    /// Control characters are dropped as well as the length bounded, because this copy can come straight from the
    /// server's envelope rather than through the MIME reader that normalizes a subject, and PostgreSQL rejects a null
    /// byte in a text value outright.
    /// </remarks>
    private static string? IndexedSubject(string? subject)
    {
        if (subject is null)
        {
            return null;
        }

        var withoutControlCharacters = new string([.. subject.Where(character => !char.IsControl(character))]);

        return MailTextBounds.TruncateAtTextElementBoundary(
            withoutControlCharacters,
            EmailSearchDocumentEntity.MaximumIndexedSubjectLength);
    }

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

    /// <summary>The values one search document holds, whichever path derived them.</summary>
    private sealed record DerivedDocument(
        string? SubjectText,
        string? ParticipantAddresses,
        string? BodyText,
        string? BodyTextBeforeTrimming,
        ExtractedEmailTextSource TextSource,
        DateTimeOffset ExtractedAt);
}
