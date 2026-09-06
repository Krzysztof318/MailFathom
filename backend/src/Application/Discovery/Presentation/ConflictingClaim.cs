// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Discovery.Presentation.Citations;

namespace MailFathom.Application.Discovery.Presentation;

/// <summary>One side of a disagreement: what part of the correspondence says, and which sources say it.</summary>
/// <remarks>
/// <para>
/// A conflict is only useful to a reader as both sides at once. Saying that sources disagree without saying what either
/// of them says leaves somebody with a warning and nothing to act on, and picking one of them is the failure the whole
/// state exists to prevent — a supplier who quoted twice has two figures, and an answer that names one has answered a
/// question nobody asked.
/// </para>
/// <para>
/// Every side names at least one source, because a side nothing backs is not a side of a disagreement: it is a claim
/// the correspondence does not make, and a block whose claim rests on nothing is
/// <see cref="PresentationSupport.Unsupported" /> rather than conflicting.
/// </para>
/// </remarks>
public sealed record ConflictingClaim
{
    /// <summary>Initializes one side of a disagreement.</summary>
    /// <param name="statement">What this side of the correspondence says.</param>
    /// <param name="sources">The citations saying it, at least one.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="sources" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="statement" /> is the unspecified default, or when the sources are empty, unspecified, or repeated.</exception>
    public ConflictingClaim(PresentationText statement, IReadOnlyList<PresentationCitationId> sources)
    {
        PresentationRequirement.Specified(statement, nameof(statement));

        var citedSources = PresentationRequirement.Sources(sources, nameof(sources));

        if (citedSources.Count is 0)
        {
            throw new ArgumentException("A side of a disagreement names the source that says it.", nameof(sources));
        }

        this.Statement = statement;
        this.Sources = citedSources;
    }

    /// <summary>Gets what this side of the correspondence says.</summary>
    public PresentationText Statement { get; }

    /// <summary>Gets the citations saying it.</summary>
    public IReadOnlyList<PresentationCitationId> Sources { get; }
}
