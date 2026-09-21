// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { AnswerBlock, TimelineEntry } from '@mailfathom/client-backend';
import { wordInstant } from '../../localization/instants';
import { useLocalization } from '../../localization/useLocalization';
import { AnswerBlockCard, UnrecognisedAnswerBlock } from '../AnswerBlockCard';
import { Citation } from './Citation';
import { timelineCounts } from './blockWording';
import { citationOrder } from './citationOrder';

// How something changed over time, as the dated events the correspondence records. It answers a question about a
// course of events rather than about a state — how the terms moved, when this started going wrong — and the whole
// point of it is that three revisions are seen rather than read about.
//
// **The order is the run's and is never sorted here.** A run that ordered by when something was agreed rather than by
// when it was mentioned has said something a date column cannot, and a client that re-sorted would throw that away
// silently. What is drawn is the reading order the plan composed.
//
// **The composition turns with the window rather than with the number of events.** Wide, the events stand beside each
// other under one rule, which is what makes a chronology readable at a glance; narrow, they stack into a column, each
// under its own rule, which is the same reading in the only shape a phone has room for. Both are the design project's
// own, and neither is a horizontal axis a narrow window would have to be scrolled along.
//
// **Every event carries its own citations** rather than the block carrying one list for all of them, because the
// question a reader has at an event is what backs *that* event. The event is not itself a control: its citations are,
// and a pressable event holding pressable citations would be a button inside a button.
//
// **The list is not windowed**, which is the repository's rule rather than an omission: the contract bounds a timeline
// at fifty entries and `frontend/src/AGENTS.md` § *Performance* windows a list that can exceed two hundred rows.

export function Timeline({ block }: { readonly block: AnswerBlock }) {
    const { locale, translate } = useLocalization();

    if (block.type !== 'timeline') {
        return <UnrecognisedAnswerBlock named={block.named} />;
    }

    const { entries, evidence } = block;
    const positionOf = citationOrder(evidence.citations, ...entries.map((entry) => entry.sources));

    return (
        <AnswerBlockCard
            label={translate('answer.timelineLabel')}
            meta={
                entries.length === 0
                    ? undefined
                    : translate(timelineCounts[new Intl.PluralRules(locale).select(entries.length)], {
                          count: new Intl.NumberFormat(locale).format(entries.length),
                      })
            }
            note={entries.length === 0 ? translate('answer.timelineEmpty') : undefined}
            state={entries.length === 0 ? 'empty' : 'ready'}
        >
            {/* An ordered list, because a chronology is one: a screen reader is told how many events there are and
                which one it is on, whichever direction the window draws them in. */}
            <ol className="flex flex-col workspace:flex-row">
                {entries.map((entry, at) => (
                    <li
                        className="flex min-w-0 flex-col gap-1.25 border-t-2 border-line-strong pt-2.5 pb-2.75 workspace:flex-1 workspace:pe-3.5 workspace:pb-0"
                        key={`${entry.occurredAt}-${String(at)}`}
                    >
                        <DatedEvent entry={entry} positionOf={positionOf} />
                    </li>
                ))}
            </ol>
        </AnswerBlockCard>
    );
}

/** One dated event: when it happened, what it happened to, what happened, and what says so. */
function DatedEvent({
    entry,
    positionOf,
}: {
    readonly entry: TimelineEntry;
    readonly positionOf: (source: string) => number;
}) {
    const { locale, translate } = useLocalization();

    // The instant the run sent is what the element carries and the reader's own spelling of it is what is drawn, which
    // is the rule `localization/instants.ts` states: a date read against a server's day is wrong for everybody else.
    const worded = wordInstant(entry.occurredAt, locale, 'stamp');

    return (
        <>
            {worded === null ? (
                <p className="text-xs text-muted">{translate('answer.timelineUndated')}</p>
            ) : (
                <time className="text-xs tabular-nums text-muted" dateTime={entry.occurredAt}>
                    {worded}
                </time>
            )}

            <p className="text-sm font-semibold text-pretty">{entry.subject}</p>

            <p className="text-xs text-muted text-pretty">{entry.summary}</p>

            {entry.sources.length === 0 ? null : (
                <p className="flex flex-wrap items-center gap-1">
                    {entry.sources.map((source) => (
                        <Citation key={source} position={positionOf(source)} source={source} />
                    ))}
                </p>
            )}
        </>
    );
}
