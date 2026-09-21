// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useState } from 'react';
import type { AnswerBlock, DeclaredSource, EvidenceEntry, SourceFreshness } from '@mailfathom/client-backend';
import { Icon } from '../../controls/Icon';
import { wordRecentInstant } from '../../localization/instants';
import type { Locale } from '../../localization/locale';
import { useLocalization, type Translate } from '../../localization/useLocalization';
import { AnswerBlockCard, UnrecognisedAnswerBlock } from '../AnswerBlockCard';
import { useAnswerSources } from '../answerSources';
import { evidenceCounts, freshnessWords, sourceKinds, sourceMediums, supportVerdicts } from './blockWording';

// The messages an answer rests on, where the correspondence rather than a reading of it is the point. Somebody
// scanning this is deciding which of them to open, so each entry says three things and says them without arithmetic:
// what the source is, the part of it that matched, and how current the local copy was.
//
// **The fragment is quoted rather than described**, which is what makes an entry checkable: it is the one text in the
// contract a reader can hold against the source and expect to find word for word. A written one is drawn in a `q`
// element so the quotation marks are the document language's own rather than a pair spelled into a catalogue entry. A
// source this deployment described rather than read is drawn in a plain paragraph and says so beside itself, under ADR
// 0030, because a machine's account of a picture presented as a quotation is a guess promoted to evidence — and the
// badge says that to somebody looking while the element says it to everything else.
//
// **A source the run never declared to this client is drawn as a private source**: the entry keeps its relevance and
// its freshness, and what it rests on is stated as unavailable rather than left blank. An entry silently shorn of its
// source would read as an answer resting on nothing, which is the one thing it is not.
//
// **The list is not windowed**, and that is the repository's own rule rather than an omission: the contract bounds one
// list at fifty entries, and `frontend/src/AGENTS.md` § *Performance* windows a list that can exceed two hundred rows
// and renders every row below that.

export function EvidenceList({ block }: { readonly block: AnswerBlock }) {
    const { locale, translate } = useLocalization();

    // The clock is read once, when the list is first drawn, for the reason `controls/ReceivedAt.tsx` gives: a render
    // is pure and the clock is not.
    const [now] = useState(() => Date.now());

    if (block.type !== 'evidenceList') {
        return <UnrecognisedAnswerBlock named={block.named} />;
    }

    const { entries, evidence } = block;
    const verdict = supportVerdicts[evidence.support];

    return (
        <AnswerBlockCard
            label={translate('answer.evidenceLabel')}
            meta={
                entries.length === 0
                    ? undefined
                    : translate(evidenceCounts[new Intl.PluralRules(locale).select(entries.length)], {
                          count: new Intl.NumberFormat(locale).format(entries.length),
                      })
            }
            note={entries.length === 0 ? translate('answer.evidenceEmpty') : undefined}
            state={entries.length === 0 ? 'empty' : 'ready'}
        >
            <span className={`flex w-fit items-center gap-1 rounded-full px-2.25 py-0.5 text-2xs ${verdict.tint}`}>
                <Icon className="size-3.25" name={verdict.icon} />
                {translate(verdict.label)}
            </span>

            <ul className="flex flex-col">
                {entries.map((entry, at) => (
                    <li className="border-b border-line-soft last:border-b-0" key={`${entry.source}-${String(at)}`}>
                        <EvidenceItem entry={entry} now={now} />
                    </li>
                ))}
            </ul>
        </AnswerBlockCard>
    );
}

/** One message the answer rests on, in whichever of its two states it is: a source this client can name, or one it cannot. */
function EvidenceItem({ entry, now }: { readonly entry: EvidenceEntry; readonly now: number }) {
    const { locale, translate } = useLocalization();
    const { sources, follow } = useAnswerSources();

    const declared = sources.get(entry.source);
    const relevance = new Intl.NumberFormat(locale, { style: 'percent' }).format(entry.relevance);
    const freshness = wordFreshness(entry.freshness, locale, now, translate);
    const name = declared?.label ?? translate('answer.sourcePrivateName');

    const said = (
        <>
            <div className="flex flex-wrap items-center gap-2">
                <span className="text-sm font-semibold">{name}</span>

                {declared === undefined ? (
                    <span className="flex items-center gap-1 rounded-full bg-rail px-2.25 py-0.5 text-2xs text-muted">
                        <Icon className="size-3.25" name="lock" />
                        {translate('answer.sourcePrivate')}
                    </span>
                ) : (
                    <SourceBadge source={declared} />
                )}
            </div>

            {declared === undefined ? (
                <p className="text-sm text-faint italic text-pretty">{translate('answer.sourcePrivateBody')}</p>
            ) : (
                <MatchedFragment declared={declared} fragment={entry.fragment} />
            )}

            <p className="flex flex-wrap gap-2.5 text-xs text-faint">
                <span>{translate('answer.relevance', { share: relevance })}</span>
                <span>{freshness}</span>
            </p>
        </>
    );

    const rows = 'flex w-full flex-col gap-1.25 py-2.5 text-start';

    // A source that can be followed is a control and a source that cannot is not one: drawing the private entry as a
    // button would offer somebody a way into mail this client was never given.
    if (declared === undefined || follow === null) {
        return <div className={rows}>{said}</div>;
    }

    return (
        <button
            aria-label={translate('answer.evidenceItem', { source: name, relevance, freshness })}
            className={`${rows} transition hover:bg-hover`}
            type="button"
            onClick={() => {
                follow(entry.source);
            }}
        >
            {said}
        </button>
    );
}

/**
 * The part of the source that matched, quoted where it is a quotation and stated plainly where it is not.
 *
 * A written source is quoted in a `q` element, which is what makes the quotation marks the document language's own
 * rather than a pair spelled into a catalogue entry. A depicted one is this deployment's description of a picture, and
 * ADR 0030 refuses presenting that as a quotation — a badge beside it says so to somebody looking, and the element
 * itself is what says so to everything else.
 */
function MatchedFragment({ declared, fragment }: { readonly declared: DeclaredSource; readonly fragment: string }) {
    return declared.medium === 'Depicted' ? (
        <p className="text-sm text-text-soft text-pretty">{fragment}</p>
    ) : (
        <q className="block text-sm text-text-soft text-pretty">{fragment}</q>
    );
}

/** What a source is, drawn as the design project's two kinds plus the one note a described picture owes a reader. */
function SourceBadge({ source }: { readonly source: DeclaredSource }) {
    const { translate } = useLocalization();
    const kind = source.kind === null ? null : sourceKinds[source.kind];
    const medium = sourceMediums[source.medium];

    return (
        <>
            {kind === null ? null : (
                <span className="flex items-center gap-1 rounded-full bg-rail px-2.25 py-0.5 text-2xs text-muted">
                    <Icon className="size-3.25" name={kind.icon} />
                    {translate(kind.label)}
                </span>
            )}

            {medium === null ? null : (
                <span className="flex items-center gap-1 rounded-full bg-warning-soft px-2.25 py-0.5 text-2xs text-warning-text">
                    <Icon className="size-3.25" name="description" />
                    {translate(medium)}
                </span>
            )}
        </>
    );
}

/**
 * How current the local copy behind one entry was, said as a word and a date rather than as a date alone.
 *
 * A freshness that states a currency without the time it was established at is read as having established nothing:
 * the two halves are one answer, and saying the word without the date it is about would state a currency the entry
 * did not carry.
 */
function wordFreshness(freshness: SourceFreshness, locale: Locale, now: number, translate: Translate): string {
    const at = wordRecentInstant(freshness.observedAt, locale, now);

    return at === null || freshness.staleness === 'Unknown'
        ? translate(freshnessWords.Unknown)
        : translate(freshnessWords[freshness.staleness], { at });
}
