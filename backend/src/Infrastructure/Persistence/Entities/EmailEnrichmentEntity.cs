// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.CodeCoverage;

namespace MailFathom.Infrastructure.Persistence.Entities;

/// <summary>That a derivation has been made about one stored occurrence, and when.</summary>
/// <remarks>
/// <para>
/// Keyed by the occurrence, which is what makes one derivation per message a property of the schema rather than a check
/// somebody has to remember: deriving from the same message twice reaches the same row, so two runs asking together
/// resolve to one record rather than to a history nobody asked for. It is also what takes a message out of the
/// selection the arrival pass walks.
/// </para>
/// <para>
/// The row exists even when the derivation found nothing worth marking, and that is the whole reason it is a row of its
/// own rather than a column on the marks. A message with no marks and no row is one still owed a derivation; a message
/// with no marks and a row is one a derivation has already answered about, and paying for that answer twice is exactly
/// what the record prevents.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class EmailEnrichmentEntity
{
    public Guid StoredEmailId { get; set; }

    /// <summary>Gets or sets the email this derivation is about, which a write leaves unset.</summary>
    /// <remarks>Optional for the reason a classification's own navigation onto its email is: the record is staged by the identifier its caller was given, and the foreign key already refuses a derivation about a message that is not there.</remarks>
    public StoredEmailEntity? StoredEmail { get; set; }

    public DateTimeOffset DerivedAt { get; set; }

    /// <summary>Gets or sets the optimistic concurrency token, which is PostgreSQL's own <c>xmin</c> rather than a column.</summary>
    /// <remarks>
    /// Two runs can reach one occurrence: an account run derives from it while a later run reaches the same message
    /// after a re-derivation cleared the record. The token is what turns that into a conflict the retry policy resolves
    /// from a fresh read instead of one writer overwriting the other's marks.
    /// </remarks>
    public uint ConcurrencyVersion { get; set; }

    /// <summary>Gets the marks the derivation produced, which is empty where it found nothing to say.</summary>
    public ICollection<EmailEnrichmentMarkEntity> Marks { get; } = [];
}
