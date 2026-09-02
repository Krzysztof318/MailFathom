// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.CodeCoverage;
using MailFathom.Domain.Spam;

namespace MailFathom.Infrastructure.Persistence.Entities;

/// <summary>One fact a classification rests on, with where it was read from.</summary>
/// <remarks>
/// A row per signal rather than one opaque column, because the whole point of the record is that the facts stay
/// separable: an operator diagnosing a wrong verdict asks which authentication method failed and which provider header
/// said what, and a serialized blob answers neither without being parsed by hand.
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class EmailSpamClassificationSignalEntity
{
    /// <summary>The greatest length a stored signal name has, which the domain value already refuses to exceed.</summary>
    internal const int MaximumNameLength = SpamSignal.MaximumNameLength;

    /// <summary>The greatest length a stored observation has, which the domain value already shortens to.</summary>
    internal const int MaximumObservationLength = SpamSignal.MaximumObservationLength;

    /// <summary>The greatest length a stored provenance origin has, which the domain value already refuses to exceed.</summary>
    internal const int MaximumOriginLength = SpamSignalProvenance.MaximumOriginLength;

    public long Id { get; set; }

    public Guid StoredEmailId { get; set; }

    /// <summary>Gets or sets the classification this signal belongs to, which a write leaves unset.</summary>
    /// <remarks>Optional for the reason the classification's own navigation onto its email is.</remarks>
    public EmailSpamClassificationEntity? Classification { get; set; }

    /// <summary>Gets or sets the position of the signal within its classification, which is the order the stages produced them in.</summary>
    /// <remarks>
    /// Kept because the order carries meaning that nothing else does: the deterministic stage's facts come first, so a
    /// record whose signals were truncated at the bound kept the ones the verdict rests on rather than an arbitrary
    /// subset.
    /// </remarks>
    public int Ordinal { get; set; }

    public SpamSignalKind Kind { get; set; }

    public required string Name { get; set; }

    /// <summary>Gets or sets what was observed, absent when the signal is the observation.</summary>
    public string? Observation { get; set; }

    public SpamSignalSource Source { get; set; }

    public required string Origin { get; set; }
}
