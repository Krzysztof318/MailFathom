// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Enrichment;
using MailFathom.CodeCoverage;

namespace MailFathom.Infrastructure.Persistence.Entities;

/// <summary>One reading of a message, with the reason behind it, what it rests on, and what produced it.</summary>
/// <remarks>
/// <para>
/// A row per reading rather than three sets of columns on the derivation, because the readings are the same shape and
/// a message carries any subset of them: columns would have meant twelve nullable ones whose emptiness a reader has to
/// interpret, and a fourth aspect would have meant four more.
/// </para>
/// <para>
/// The source and the origin are stored beside the text rather than derived from it, which is what makes a rule's
/// verdict and a model's distinguishable in the data. A query can select every mark a given rule produced, or every
/// mark no rule stands behind, without reading the text of any of them.
/// </para>
/// <para>
/// The evidence is a <c>uuid[]</c> of the passages the mark rests on, in the order the producer named them, rather than
/// a table of its own. A mark's evidence is read whole with the mark and never joined to, so a row per citation would
/// have bought a join and an ordinal column to preserve an order the array already carries; what a query still does
/// with it is ask which marks cite a passage, which <c>= ANY</c> answers.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class EmailEnrichmentMarkEntity
{
    /// <summary>The greatest length a stored reading or its reason has, which the domain value already shortens to.</summary>
    internal const int MaximumTextLength = EmailEnrichmentMark.MaximumTextLength;

    /// <summary>The greatest length a stored provenance origin has, which the domain value already refuses to exceed.</summary>
    internal const int MaximumOriginLength = EmailEnrichmentProvenance.MaximumOriginLength;

    public long Id { get; set; }

    public Guid StoredEmailId { get; set; }

    /// <summary>Gets or sets the derivation this mark belongs to, which a write leaves unset.</summary>
    /// <remarks>Optional for the reason the derivation's own navigation onto its email is.</remarks>
    public EmailEnrichmentEntity? Enrichment { get; set; }

    public EmailEnrichmentAspect Aspect { get; set; }

    public required string Text { get; set; }

    public required string Reason { get; set; }

    /// <summary>Gets or sets when the commitment falls due, absent on every aspect but a commitment and on a commitment that named no date.</summary>
    public DateTimeOffset? DueAt { get; set; }

    public EmailEnrichmentSource Source { get; set; }

    public required string Origin { get; set; }

    /// <summary>Gets or sets the passages the mark rests on, in the order the producer named them.</summary>
    public required Guid[] Evidence { get; set; }
}
