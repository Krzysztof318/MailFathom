// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json;
using MailFathom.Application.Discovery.Planning;
using MailFathom.Application.Discovery.Presentation;
using MailFathom.Application.Discovery.Presentation.Blocks;
using MailFathom.Application.Discovery.Presentation.Citations;
using MailFathom.Application.Discovery.Runs;
using MailFathom.Application.Emails.Search;

namespace MailFathom.AI.Discovery;

/// <summary>Turns what a composing agent wrote into the plan a client draws, or into a plan saying the sources do not answer the question.</summary>
/// <remarks>
/// <para>
/// <strong>It never fails.</strong> A model that wrote prose instead of JSON, cited a source nobody offered it, proposed
/// a column the catalogue does not hold, or never answered at all produces a result rather than an error — one whose
/// blocks say what the correspondence does for them, which for every one of those cases is nothing. That is the
/// product's own rule stated as code: an absence of evidence is never filled in with text the model produced.
/// </para>
/// <para>
/// It is also what makes the composition testable without a provider. Every rule below is a pure function of the
/// answer text, the sources the run declared, and what it read of its own accounts, so the answers a provider produces
/// once in a thousand runs are ordinary examples here.
/// </para>
/// <para>
/// Four judgements are the composition's own rather than a model's, and none of them is negotiable by what a model
/// wrote. The support follows from whether the cited sources resolve, whether they disagree, and how current they are.
/// A disagreement is kept as both of its sides rather than resolved into one. The confidence is capped by that support,
/// so a model cannot report a settled answer over a contradiction. And the freshness of a block is the freshness of the
/// accounts its own sources were read from.
/// </para>
/// </remarks>
internal static class DiscoveryCompositionReading
{
    private const string JsonFence = "```";

    /// <summary>Reads a result out of an agent's answer, falling back to a result saying the sources do not answer wherever the answer cannot be believed.</summary>
    /// <param name="answerText">What the agent wrote, which may be empty, fenced, or surrounded by prose.</param>
    /// <param name="plan">What the question was read as, which fixes what the result is composed of.</param>
    /// <param name="sources">The sources the run declared, which are the only ones a claim may rest on.</param>
    /// <param name="evidence">What running the retrieval plan took, which is where two of the limitations come from.</param>
    /// <param name="coverage">What the run read, one entry per account its scope reached.</param>
    /// <returns>The plan a client draws.</returns>
    /// <exception cref="ArgumentNullException">Thrown when any argument but <paramref name="answerText" /> is <see langword="null" />.</exception>
    internal static PresentationPlan Read(
        string? answerText,
        DiscoveryRunPlan plan,
        IReadOnlyList<DiscoveryComposedSource> sources,
        DiscoveryEvidence evidence,
        IReadOnlyList<AccountCoverage> coverage)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(coverage);

        var everySource = sources.ToDictionary(source => source.Citation.Id.Value, StringComparer.Ordinal);
        var freshness = new DiscoveryFreshnessIndex(sources, coverage);
        var document = ReadDocument(answerText);

        var cited = Resolve(document?.Sources, everySource);

        // Everything inside the block refers to what the block rests on and to nothing else: a side, an event, or a
        // cell naming a source the answer never cited would be a reference the block's own evidence does not carry,
        // which the contract refuses outright rather than presents.
        var restedOn = cited.ToDictionary(source => source.Citation.Id.Value, StringComparer.Ordinal);

        var conflictingClaims = ReadConflictingClaims(document?.Conflict, restedOn);
        var support = SupportOf(cited, conflictingClaims, freshness);

        var evidenceOf = new PresentationEvidence(
            support,
            [.. cited.Select(source => source.Citation.Id)],
            freshness.Of(cited),
            support is PresentationSupport.Conflicting ? conflictingClaims : []);

        IReadOnlyList<PresentationBlock> blocks =
        [
            OpeningBlock(plan.Intent, document, evidenceOf, restedOn, support),
            .. EvidenceList(sources, freshness),
        ];

        return PresentationPlan.Compose(
            blocks,
            [.. sources.Select(source => source.Citation)],
            coverage,
            Limitations(evidence, coverage));
    }

    /// <summary>Composes the opening block the intent names, or the answer that says the sources do not settle the question.</summary>
    /// <remarks>
    /// The fallback is one block rather than one per intent, because every intent whose material a model did not supply
    /// reaches the same honest answer: nothing here can compose a timeline out of no events or a comparison out of no
    /// rows, and inventing either is the failure this whole contract exists to prevent. A question looking for files is
    /// always answered this way today, because a passage carries the message it was cut from and not the attachment
    /// within it, so a gallery entry would name a file this run cannot resolve.
    /// <para>
    /// An unsupported answer takes it as well, whatever the intent and whatever material the model supplied. A model
    /// told to leave its sources empty where the extracts do not answer may still fill in events or rows, and both an
    /// entry and a cell may name no source at all — so a shaped block over an unsupported answer would present dated
    /// events, or a comparison, written entirely by the model and cited to nothing. That is the same substitution
    /// <see cref="Answer" /> refuses for prose, and it is refused here in the one place both shapes pass through.
    /// </para>
    /// </remarks>
    private static PresentationBlock OpeningBlock(
        DiscoveryIntent intent,
        DiscoveryResultDocument? document,
        PresentationEvidence evidence,
        Dictionary<string, DiscoveryComposedSource> restedOn,
        PresentationSupport support)
    {
        PresentationBlock? shaped = support is PresentationSupport.Unsupported
            ? null
            : intent.OpensWith.Identity switch
            {
                PresentationBlockType.TimelineIdentity => Timeline(document?.Events, evidence, restedOn),
                PresentationBlockType.FactTableIdentity => FactTable(document, evidence, restedOn),
                _ => null,
            };

        return shaped ?? Answer(document, evidence, support);
    }

    /// <summary>Composes the synthesized answer, holding what a model claimed about it to what its sources support.</summary>
    /// <remarks>
    /// The text is the model's own words where it wrote any and the answer is backed, and a stated absence otherwise —
    /// an unsupported block's own prose would be exactly the sentence nobody wrote that the state exists to refuse. The
    /// sentence used instead is deliberately about the run rather than about the subject, so it carries no claim.
    /// </remarks>
    private static AnswerBlock Answer(
        DiscoveryResultDocument? document,
        PresentationEvidence evidence,
        PresentationSupport support)
    {
        var text = support is not PresentationSupport.Unsupported
            && PresentationText.TryCreate(document?.Answer, out var written)
            ? written
            : UnansweredText;

        return new AnswerBlock(evidence, text, ConfidenceOf(document?.Confidence, support));
    }

    /// <summary>Composes the course of events, or nothing where the model named none this run can stand behind.</summary>
    private static TimelineBlock? Timeline(
        IReadOnlyList<DiscoveryEventDocument>? events,
        PresentationEvidence evidence,
        Dictionary<string, DiscoveryComposedSource> restedOn)
    {
        IReadOnlyList<TimelineEntry> entries =
        [
            .. (events ?? [])
                .Where(entry => entry is not null)
                .Select(entry => Entry(entry, restedOn))
                .OfType<TimelineEntry>()
                .Take(TimelineBlock.MaxEntries),
        ];

        return entries.Count is 0 ? null : new TimelineBlock(evidence, entries);
    }

    private static TimelineEntry? Entry(
        DiscoveryEventDocument entry,
        Dictionary<string, DiscoveryComposedSource> restedOn)
    {
        if (entry.OccurredAt is not { } occurredAt
            || !PresentationText.TryCreate(entry.Summary, out var summary)
            || !PresentationText.TryCreate(entry.Subject, out var subject))
        {
            return null;
        }

        return new TimelineEntry(occurredAt, summary, subject, References(entry.Sources, restedOn));
    }

    /// <summary>Composes the comparison, or nothing where the columns or the rows are not a table this contract admits.</summary>
    /// <remarks>
    /// A row whose cell count does not match the columns is dropped rather than padded, because a padded row asserts
    /// that the correspondence said nothing about a column when what happened is that a model wrote a short row.
    /// </remarks>
    private static FactTableBlock? FactTable(
        DiscoveryResultDocument? document,
        PresentationEvidence evidence,
        Dictionary<string, DiscoveryComposedSource> restedOn)
    {
        IReadOnlyList<FactTableColumn> columns =
        [
            .. (document?.Columns ?? [])
                .Select(name => FactTableColumn.TryParse(name, out var column) ? column : default)
                .Where(column => column.IsSpecified)
                .Distinct()
                .Take(FactTableBlock.MaxColumns),
        ];

        if (columns.Count is 0)
        {
            return null;
        }

        IReadOnlyList<FactTableRow> rows =
        [
            .. (document?.Rows ?? [])
                .Where(row => row?.Cells is not null && row.Cells.Count == columns.Count)
                .Select(row => new FactTableRow([.. row.Cells!.Select(cell => Cell(cell, restedOn))]))
                .Take(FactTableBlock.MaxRows),
        ];

        return rows.Count is 0 ? null : new FactTableBlock(evidence, columns, rows);
    }

    private static FactTableCell Cell(
        DiscoveryCellDocument cell,
        Dictionary<string, DiscoveryComposedSource> restedOn)
    {
        if (cell is null || !PresentationText.TryCreate(cell.Value, out var value))
        {
            return new FactTableCell(value: null, []);
        }

        return new FactTableCell(value, References(cell.Sources, restedOn));
    }

    /// <summary>Composes the messages the answer rests on, which every result carries and no model writes.</summary>
    /// <remarks>
    /// Built from what retrieval returned rather than from what the model cited, because a reader checking a thin
    /// answer needs to see the mail the run actually read. Its own support is stated against those same sources, so an
    /// evidence list over a mailbox that is behind says so exactly as the answer above it does.
    /// </remarks>
    private static IReadOnlyList<EvidenceListBlock> EvidenceList(
        IReadOnlyList<DiscoveryComposedSource> sources,
        DiscoveryFreshnessIndex freshness)
    {
        if (sources.Count is 0)
        {
            return [];
        }

        // Bounded by what one block may cite rather than by what a list may hold, the two being different numbers: the
        // entries and their citations are the same set here, so the smaller of the two is the one that binds.
        var listed = sources.Take(PresentationEvidence.MaxCitations).ToArray();
        var blockFreshness = freshness.Of(listed);

        IReadOnlyList<EvidenceEntry> entries =
        [
            .. listed.Select(source => new EvidenceEntry(
                source.Citation.Id,
                Quoted(source),
                source.Relevance,
                freshness.Of([source]))),
        ];

        return
        [
            new EvidenceListBlock(
                new PresentationEvidence(
                    blockFreshness.Staleness is PresentationStaleness.Stale
                        ? PresentationSupport.Stale
                        : PresentationSupport.Supported,
                    [.. listed.Select(source => source.Citation.Id)],
                    blockFreshness),
                entries),
        ];
    }

    /// <summary>Cuts an extract down to what one text of the contract may carry, without inventing anything in its place.</summary>
    private static PresentationText Quoted(DiscoveryComposedSource source)
    {
        var extract = source.Extract.Length > PresentationText.MaxLength
            ? source.Extract[..PresentationText.MaxLength]
            : source.Extract;

        return PresentationText.TryCreate(extract, out var quoted) ? quoted : source.Citation.Label;
    }

    /// <summary>Says what the correspondence does for the answer, from what resolved rather than from what was claimed.</summary>
    /// <remarks>
    /// A conflict outranks staleness, because a reader told only that a mailbox is behind would go and synchronize it
    /// instead of reading the two figures that already disagree. Staleness outranks plain support for the reason the
    /// support catalogue gives: the two invite different acts.
    /// </remarks>
    private static PresentationSupport SupportOf(
        IReadOnlyList<DiscoveryComposedSource> cited,
        IReadOnlyList<ConflictingClaim> conflictingClaims,
        DiscoveryFreshnessIndex freshness)
    {
        if (cited.Count is 0)
        {
            return PresentationSupport.Unsupported;
        }

        if (cited.Count >= 2 && conflictingClaims.Count >= 2)
        {
            return PresentationSupport.Conflicting;
        }

        return freshness.Of(cited).Staleness is PresentationStaleness.Stale
            ? PresentationSupport.Stale
            : PresentationSupport.Supported;
    }

    /// <summary>Reads the band a model reported and caps it at what its own sources allow.</summary>
    /// <remarks>
    /// The cap is the whole definition of the value and it is applied here rather than asked for: a model reporting a
    /// settled answer over a contradiction, over sources that are all behind, or over no source at all is reporting
    /// its own fluency. An unreadable band is read as moderate rather than as high, so a malformed answer never
    /// arrives more confident than a well-formed one.
    /// </remarks>
    private static PresentationConfidence ConfidenceOf(string? reported, PresentationSupport support)
    {
        var band = reported?.Trim().ToUpperInvariant() switch
        {
            "HIGH" => PresentationConfidence.High,
            "LOW" => PresentationConfidence.Low,
            _ => PresentationConfidence.Moderate,
        };

        return support switch
        {
            PresentationSupport.Unsupported => PresentationConfidence.Low,
            PresentationSupport.Conflicting or PresentationSupport.Stale =>
                band is PresentationConfidence.High ? PresentationConfidence.Moderate : band,
            _ => band,
        };
    }

    /// <summary>Reads the sides of a disagreement, keeping only the ones that name sources the answer rests on.</summary>
    private static IReadOnlyList<ConflictingClaim> ReadConflictingClaims(
        IReadOnlyList<DiscoveryConflictDocument>? sides,
        Dictionary<string, DiscoveryComposedSource> restedOn) =>
    [
        .. (sides ?? [])
            .Where(side => side is not null)
            .Select(side => ConflictingClaimOf(side, restedOn))
            .OfType<ConflictingClaim>()
            .Take(PresentationEvidence.MaxConflictingClaims),
    ];

    private static ConflictingClaim? ConflictingClaimOf(
        DiscoveryConflictDocument side,
        Dictionary<string, DiscoveryComposedSource> restedOn)
    {
        if (!PresentationText.TryCreate(side.Statement, out var statement))
        {
            return null;
        }

        var sources = References(side.Sources, restedOn);

        return sources.Count is 0 ? null : new ConflictingClaim(statement, sources);
    }

    /// <summary>States what the run knows about its own reach, from what it observed rather than from what it was told.</summary>
    /// <remarks>
    /// Both members are read off the run itself. A lookup the deployment refused is mail the answer would have rested
    /// on and did not, and a ranking that fell back to words alone is a question answered without any reading of
    /// meaning — which is why a thin answer to a well-posed question is worth saying out loud.
    /// </remarks>
    private static List<PresentationLimitation> Limitations(
        DiscoveryEvidence evidence,
        IReadOnlyList<AccountCoverage> coverage)
    {
        var limitations = new List<PresentationLimitation>(3);

        if (coverage.Any(account => account.Freshness.Staleness is PresentationStaleness.Stale))
        {
            limitations.Add(PresentationLimitation.LocalCopyBehind);
        }

        if (evidence.LookupsRefused > 0)
        {
            limitations.Add(PresentationLimitation.SourcesUnavailable);
        }

        if (evidence.RetrievalMode is EmailSearchRetrievalMode.Lexical)
        {
            limitations.Add(PresentationLimitation.SemanticRankingUnavailable);
        }

        return limitations;
    }

    private static IReadOnlyList<DiscoveryComposedSource> Resolve(
        IReadOnlyList<string>? named,
        Dictionary<string, DiscoveryComposedSource> available) =>
    [
        .. (named ?? [])
            .Select(name => name?.Trim() ?? string.Empty)
            .Distinct(StringComparer.Ordinal)
            .Select(name => available.GetValueOrDefault(name))
            .OfType<DiscoveryComposedSource>()
            .Take(PresentationEvidence.MaxCitations),
    ];

    private static IReadOnlyList<PresentationCitationId> References(
        IReadOnlyList<string>? named,
        Dictionary<string, DiscoveryComposedSource> restedOn) =>
        [.. Resolve(named, restedOn).Select(source => source.Citation.Id)];

    private static DiscoveryResultDocument? ReadDocument(string? answerText)
    {
        if (Unfenced(answerText) is not { } json)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize(json, DiscoveryResultJsonContext.Default.DiscoveryResultDocument);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Finds the JSON object inside whatever the model wrote around it.</summary>
    /// <remarks>
    /// The same reading the planning answer gets, and for the same reason: a model told to answer with one object still
    /// fences it, prefaces it, or writes a sentence after it often enough that treating any of those as no answer would
    /// throw away a usable result.
    /// </remarks>
    private static string? Unfenced(string? answerText)
    {
        if (string.IsNullOrWhiteSpace(answerText))
        {
            return null;
        }

        var text = answerText.Replace(JsonFence, string.Empty, StringComparison.Ordinal);
        var opening = text.IndexOf('{', StringComparison.Ordinal);
        var closing = text.LastIndexOf('}');

        return opening >= 0 && closing > opening ? text[opening..(closing + 1)] : null;
    }

    /// <summary>What an unsupported answer says, which is a statement about the run rather than about the question.</summary>
    private static PresentationText UnansweredText { get; } =
        PresentationText.Create("The mail this run read does not answer the question.");
}
