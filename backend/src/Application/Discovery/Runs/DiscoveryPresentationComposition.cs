// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Discovery.Presentation;
using MailFathom.Application.Discovery.Presentation.Blocks;
using MailFathom.Application.Discovery.Presentation.Citations;
using MailFathom.Application.Retrieval;

namespace MailFathom.Application.Discovery.Runs;

/// <summary>Turns what a run retrieved into the sources it declares and the blocks that rest on them.</summary>
/// <remarks>
/// <para>
/// The whole of what a run can present without a model. Every value here is either quoted from the mail or is a count
/// of what retrieval did, which is what makes the result checkable: the fragment a reader is shown is the extract the
/// run actually answered from, word for word, rather than a description of it.
/// </para>
/// <para>
/// One block comes out of it today — the evidence list — because it is the one whose contract is satisfied by
/// correspondence alone. The blocks that state something about the correspondence rather than showing it need a
/// derivation, and a composition that filled an answer, a timeline, or a table from passage order would be inventing
/// the very facts a citation exists to make checkable.
/// </para>
/// <para>
/// Nothing here is a plan. A run publishes its citations and its blocks as they become ready, so what a client assembles
/// is the plan and what this produces is its parts, in an order a reader can render as it arrives: a source is declared
/// before the block naming it, which is the same rule <see cref="PresentationPlan" /> enforces over a whole one.
/// </para>
/// </remarks>
public static class DiscoveryPresentationComposition
{
    /// <summary>The greatest number of sources one run declares, which is what one block may rest on.</summary>
    /// <remarks>
    /// Taken from the block bound rather than from the plan's, because every source a run declares is cited by the
    /// evidence list and a citation no block names would be a source a reader cannot reach from anything.
    /// </remarks>
    public const int MaximumCitations = PresentationEvidence.MaxCitations;

    /// <summary>Composes the sources a run declares from the passages it retrieved, one per distinct message.</summary>
    /// <param name="evidence">What the run retrieved.</param>
    /// <returns>The citations, in the order the run first reached each message, and empty where it retrieved nothing.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="evidence" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// One citation per message rather than one per passage, so two facts drawn from one message are visibly the same
    /// source. The target is the message itself rather than a passage, because a retrieved extract carries no persisted
    /// passage identity to point at — the coordinate a fragment citation needs is one a derivation records, not one
    /// retrieval hands over.
    /// </remarks>
    public static IReadOnlyList<PresentationCitation> CitationsFor(DiscoveryEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);

        return
        [
            .. evidence.Passages
                .DistinctBy(static passage => passage.StoredEmailId)
                .Take(MaximumCitations)
                .Select((passage, position) => new PresentationCitation(
                    PresentationCitationId.Create($"s{position + 1}"),
                    new EmailCitationTarget(passage.StoredEmailId),
                    LabelFor(passage))),
        ];
    }

    /// <summary>Composes the blocks a run presents, in the order they are read.</summary>
    /// <param name="evidence">What the run retrieved.</param>
    /// <param name="citations">The sources the run declared, which is what the blocks name.</param>
    /// <returns>The blocks, and empty where the run retrieved nothing to present.</returns>
    /// <exception cref="ArgumentNullException">Thrown when either argument is <see langword="null" />.</exception>
    /// <remarks>
    /// A run that found nothing composes no block rather than a block saying so. Whether an unanswered question is
    /// presented as an empty result or as a stated absence is a judgement about the answer rather than about the
    /// correspondence, and this composition makes none.
    /// </remarks>
    public static IReadOnlyList<PresentationBlock> BlocksFor(
        DiscoveryEvidence evidence,
        IReadOnlyList<PresentationCitation> citations)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(citations);

        var entries = EntriesFor(evidence, citations);

        if (entries.Count is 0)
        {
            return [];
        }

        var evidenceOfTheList = new PresentationEvidence(
            PresentationSupport.Supported,
            [.. entries.Select(static entry => entry.Source).Distinct()],
            PresentationFreshness.Unknown);

        return [new EvidenceListBlock(evidenceOfTheList, entries)];
    }

    /// <summary>Reads the passages of the messages a run declared into the entries of an evidence list.</summary>
    /// <remarks>
    /// A passage whose message was not declared is dropped rather than cited under a name the run never published, which
    /// is what keeps every reference resolvable. So is one whose extract is not text a plan may carry — a message body
    /// that decoded to control characters is not something to show a reader, and the message stays cited by the entries
    /// its other passages produced.
    /// </remarks>
    private static IReadOnlyList<EvidenceEntry> EntriesFor(
        DiscoveryEvidence evidence,
        IReadOnlyList<PresentationCitation> citations)
    {
        var declared = citations.ToDictionary(
            static citation => citation.Target.Email,
            static citation => citation.Id);

        EmailKnowledgePassage[] presentable =
        [
            .. evidence.Passages
                .Where(passage => declared.ContainsKey(passage.StoredEmailId))
                .Take(EvidenceListBlock.MaxEntries),
        ];

        return
        [
            .. presentable
                .Select((passage, position) => PresentationText.TryCreate(passage.Text, out var fragment)
                    ? new EvidenceEntry(
                        declared[passage.StoredEmailId],
                        fragment,
                        RelevanceOf(position, presentable.Length),
                        PresentationFreshness.Unknown)
                    : null)
                .OfType<EvidenceEntry>(),
        ];
    }

    /// <summary>States how well an entry answers the question, from the place retrieval gave it.</summary>
    /// <remarks>
    /// The rank expressed as a fraction, which is exactly what the block publishes it as: retrieval hands its passages
    /// over best-ranked first and attaches no score, so the ordering is the whole of what is known about how well each
    /// one answers.
    /// </remarks>
    private static double RelevanceOf(int position, int count)
    {
        // ponytail: an ordinal stands in for a judged score until a derivation produces one, which is #1173.
        return (double)(count - position) / count;
    }

    /// <summary>Says what a client prints where a source is named.</summary>
    /// <remarks>
    /// The subject wherever the message carried one a plan may carry, because it is what a reader recognizes a message
    /// by. Where it did not — a message with no subject, or one whose subject is not presentable text — the deployment's
    /// own name for where the message was read from stands in, so a source always reads as something before it is
    /// followed and nothing about a sender's own text decides whether it can be.
    /// </remarks>
    private static PresentationText LabelFor(EmailKnowledgePassage passage) =>
        PresentationText.TryCreate(passage.Subject, out var subject)
            ? subject
            : PresentationText.Create($"{passage.AccountId.Value} / {passage.FolderAlias.Value}");
}
