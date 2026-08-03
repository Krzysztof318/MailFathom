// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Extraction;
using MailFathom.CodeCoverage;
using NpgsqlTypes;

namespace MailFathom.Infrastructure.Persistence.Entities;

/// <summary>The derived text one stored email contributes to lexical search, and the vector built from it.</summary>
/// <remarks>
/// <para>
/// The document is a table of its own for the reason raw MIME is: it is large, it is read by search alone, and every
/// ordinary mailbox query would otherwise carry a body's worth of text and its search vector through the change tracker
/// on its way to a timeline that shows neither.
/// </para>
/// <para>
/// Its columns are derived from the message and inherit that message's classification, retention, export, and erasure
/// obligations whole. Nothing here is anonymous, and the cascade from the stored email is what keeps a deletion of the
/// message a deletion of everything derived from it.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class EmailSearchDocumentEntity
{
    /// <summary>The greatest number of subject characters the index covers.</summary>
    /// <remarks>
    /// Nothing between the mail server and this row bounds a subject, and the whole document has to stay well inside
    /// PostgreSQL's one-megabyte limit on a <c>tsvector</c>. The bound applies to the indexed copy only; the stored
    /// email keeps the subject a message actually wrote.
    /// </remarks>
    internal const int MaximumIndexedSubjectLength = 2000;

    /// <summary>The greatest number of participant addresses the index covers, counted across every role.</summary>
    /// <remarks>
    /// A message addressed to more mailboxes than this is a list expansion whose members no search asks about
    /// individually, and the whole expansion would otherwise dominate the document's vector.
    /// </remarks>
    internal const int MaximumIndexedParticipantAddresses = 64;

    public Guid StoredEmailId { get; set; }

    public required StoredEmailEntity StoredEmail { get; set; }

    /// <summary>Gets or sets the bounded subject copy the search vector covers.</summary>
    public string? SubjectText { get; set; }

    /// <summary>Gets or sets the bounded, space-separated normalized participant addresses the search vector covers.</summary>
    /// <remarks>
    /// Materialized as one text value rather than read from the stored email's array columns because a generated column
    /// must be immutable and PostgreSQL's array-to-text functions are not: they call element output functions that need
    /// not be. Building the value where the addresses are already in hand keeps the column definition trivial.
    /// </remarks>
    public string? ParticipantAddresses { get; set; }

    /// <summary>Gets or sets the body text after quoted history and signatures were removed, which is what the search vector covers.</summary>
    public string? BodyText { get; set; }

    /// <summary>Gets or sets the body text as it was extracted, before trimming, which no index covers.</summary>
    /// <remarks>
    /// Retained rather than replaced: trimming is heuristic, and without the untrimmed reading an over-aggressive cut
    /// would be the only surviving one until somebody re-derived the text from the raw MIME.
    /// </remarks>
    public string? BodyTextBeforeTrimming { get; set; }

    /// <summary>Gets or sets where the text came from, or why the message yielded none.</summary>
    public ExtractedEmailTextSource TextSource { get; set; }

    /// <summary>Gets or sets when this document was derived, which is what tells a re-derivation from an original extraction apart.</summary>
    public DateTimeOffset ExtractedAt { get; set; }

    /// <summary>Gets or sets the search vector PostgreSQL generates from the columns above.</summary>
    /// <remarks>
    /// Never assigned by MailFathom. The column is <c>GENERATED ALWAYS ... STORED</c>, so PostgreSQL recomputes it from
    /// this row on every insert and update and no code path can leave it disagreeing with the text beside it.
    /// </remarks>
    public NpgsqlTsVector SearchVector { get; set; } = null!;
}
