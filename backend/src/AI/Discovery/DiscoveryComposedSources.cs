// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Discovery.Presentation;
using MailFathom.Application.Discovery.Presentation.Citations;
using MailFathom.Application.Retrieval;
using MailFathom.Domain.Accounts;

namespace MailFathom.AI.Discovery;

/// <summary>One source a run may cite: the citation the plan declares, the extract the model is shown, and where it was read from.</summary>
/// <param name="Citation">The citation the plan declares, under the name the model is asked to cite it by.</param>
/// <param name="AccountId">The account the extract was read from, which is how the block's freshness is found.</param>
/// <param name="Extract">The passage itself, which is what the model is shown and what an evidence entry quotes.</param>
/// <param name="Relevance">Where the extract stood in the retrieval's own order, on the scale the contract states it on.</param>
internal sealed record DiscoveryComposedSource(
    PresentationCitation Citation,
    MailAccountId AccountId,
    string Extract,
    double Relevance);

/// <summary>Declares the sources one run may cite, before a model is shown anything.</summary>
/// <remarks>
/// <para>
/// Citations are minted here rather than by the model, which is the whole reason a composed answer can be checked. The
/// model is shown a name per extract and may only cite those names; a name it invents resolves to nothing and the claim
/// resting on it is read as resting on nothing. Nothing a model writes ever becomes a citation target.
/// </para>
/// <para>
/// One citation per message rather than per extract, because that is what a passage can be followed to today: a passage
/// carries the message it was cut from and not the persisted identity of the cut, so a fragment target would be a
/// coordinate this run does not hold. Two extracts of one message therefore share a source, which is also what a reader
/// checking two facts against one message wants to see.
/// </para>
/// <para>
/// Every source is <see cref="PresentationSourceMedium.Written" /> here, because every passage this retrieval returns
/// is text somebody wrote. A description of a picture becomes reachable when retrieval admits one, and the medium is
/// declared on the citation so that the ranking
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0030-describing-an-image-attachment-in-words-and-ranking-a-depicted-match-below-a-written-one.md">ADR 0030</see>
/// fixes travels with the source rather than being re-derived per surface.
/// </para>
/// </remarks>
internal static class DiscoveryComposedSources
{
    /// <summary>Declares one source per distinct message the run retrieved, in the order retrieval ranked them.</summary>
    /// <param name="passages">The passages the run may answer from, best-ranked first.</param>
    /// <returns>The sources, bounded by what one block may rest on.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="passages" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// Bounded by <see cref="PresentationEvidence.MaxCitations" /> rather than by the retrieval's own ceiling, because a
    /// block may rest on no more than that and an answer resting on more messages than a person will open is a
    /// retrieval that failed to narrow rather than a better answer.
    /// </remarks>
    internal static IReadOnlyList<DiscoveryComposedSource> Declare(IReadOnlyList<EmailKnowledgePassage> passages)
    {
        ArgumentNullException.ThrowIfNull(passages);

        var ranked = passages
            .GroupBy(passage => passage.StoredEmailId)
            .Take(PresentationEvidence.MaxCitations)
            .Select(message => message.First())
            .ToArray();

        return
        [
            .. ranked.Select((passage, rank) => new DiscoveryComposedSource(
                new PresentationCitation(
                    PresentationCitationId.Create($"s{rank + 1}"),
                    new EmailCitationTarget(passage.StoredEmailId),
                    LabelOf(passage),
                    PresentationSourceMedium.Written),
                passage.AccountId,
                passage.Text,
                RelevanceOf(rank, ranked.Length))),
        ];
    }

    /// <summary>Names a source in the words the correspondence itself used.</summary>
    /// <remarks>
    /// The subject first, because that is what a person recognizes a message by, and the opening of the extract where a
    /// message carried none. Both are mail the sender wrote, which is the point: a label this deployment composed
    /// itself would be one language's words on a screen the client localizes, and there is no honest third choice.
    /// The message's own identifier is the last resort and is never blank, so a label always exists.
    /// </remarks>
    private static PresentationText LabelOf(EmailKnowledgePassage passage)
    {
        if (PresentationText.TryCreate(passage.Subject, out var subject))
        {
            return subject;
        }

        var opening = passage.Text.Split('\n', 2)[0];

        return PresentationText.TryCreate(opening, out var quoted)
            ? quoted
            : PresentationText.Create(passage.StoredEmailId.Value.ToString());
    }

    /// <summary>States where an extract stood in the retrieval's own order, on the scale the contract asks for.</summary>
    /// <remarks>
    /// Retrieval publishes an order and no score, so this is the order expressed as a number rather than a measurement
    /// of anything. It is derived rather than asked of a model on purpose: a relevance a model wrote would be a figure
    /// invented about the ranking that produced its own input.
    /// </remarks>
    private static double RelevanceOf(int rank, int count) => (count - rank) / (double)count;
}
