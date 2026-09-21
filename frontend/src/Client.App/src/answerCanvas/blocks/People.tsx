// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useState } from 'react';
import type { AnswerBlock, PersonEntry } from '@mailfathom/client-backend';
import { Icon } from '../../controls/Icon';
import { SenderAvatar } from '../../controls/SenderAvatar';
import { wordRecentInstant } from '../../localization/instants';
import { useLocalization } from '../../localization/useLocalization';
import { useReadingZone } from '../../localization/useReadingZone';
import { AnswerBlockCard, UnrecognisedAnswerBlock } from '../AnswerBlockCard';
import { Citation } from './Citation';
import { personCounts, supportVerdicts } from './blockWording';
import { citationOrder } from './citationOrder';

// The people a question is about, where the answer is about somebody rather than about a message. What it draws is
// three things per person and nothing more: who they are, where they stand in the correspondence, and when they were
// last in contact.
//
// **A relationship is read out of the mail rather than known**, which is why every row carries the sources it was read
// from. The contract gives each entry its own citations — the block's list is what the block as a whole rests on — so a
// reader asking why the run thinks somebody is the person waiting for a reply has the messages that say so, at the row
// that says it. The design project's own row draws no such chip, and this is the one element added to it: without the
// citations there is nowhere to check an assertion about a person, which is the promise the block exists to keep.
//
// **A row is not a control.** The design draws it as one because its prototype opens the person, and nothing on this
// canvas has anywhere to open them to — following a *source* is what the run declared and what `Citation` offers. A row
// drawn as a button that did nothing would be the affordance without the act.

export function People({ block }: { readonly block: AnswerBlock }) {
    const { locale, translate } = useLocalization();

    // The clock is read once, when the block is first drawn, for the reason `controls/ReceivedAt.tsx` gives: a render
    // is pure and the clock is not.
    const [now] = useState(() => Date.now());

    if (block.type !== 'people') {
        return <UnrecognisedAnswerBlock named={block.named} />;
    }

    const { entries, evidence } = block;
    const verdict = supportVerdicts[evidence.support];
    const positionOf = citationOrder(evidence.citations, ...entries.map((entry) => entry.sources));

    return (
        <AnswerBlockCard
            label={translate('answer.peopleLabel')}
            meta={
                entries.length === 0
                    ? undefined
                    : translate(personCounts[new Intl.PluralRules(locale).select(entries.length)], {
                          count: new Intl.NumberFormat(locale).format(entries.length),
                      })
            }
            note={entries.length === 0 ? translate('answer.peopleEmpty') : undefined}
            state={entries.length === 0 ? 'empty' : 'ready'}
        >
            <span className={`flex w-fit items-center gap-1 rounded-full px-2.25 py-0.5 text-2xs ${verdict.tint}`}>
                <Icon className="size-3.25" name={verdict.icon} />
                {translate(verdict.label)}
            </span>

            <ul className="flex flex-col">
                {entries.map((entry, at) => (
                    // A person carries no identity of its own where the correspondence named them without an address,
                    // so the place the entry holds is what keys it: the block is replaced whole when the run composes
                    // it again rather than reordered under a reader.
                    <li
                        className="flex flex-wrap items-center gap-3.5 border-b border-line-soft py-2.5 last:border-b-0"
                        key={`${entry.person.address ?? entry.person.displayName}-${String(at)}`}
                    >
                        <Person entry={entry} now={now} positionOf={positionOf} />
                    </li>
                ))}
            </ul>
        </AnswerBlockCard>
    );
}

/** One person: who they are, where they stand, when they were last in contact, and what says so. */
function Person({
    entry,
    now,
    positionOf,
}: {
    readonly entry: PersonEntry;
    readonly now: number;

    /** Where a source stands among the ones the block names, which is the number its chip draws. */
    readonly positionOf: (source: string) => number;
}) {
    const { locale, translate } = useLocalization();
    const timeZone = useReadingZone();
    const last = wordRecentInstant(entry.lastContactAt, locale, now, timeZone);

    return (
        <>
            <SenderAvatar address={entry.person.address} displayName={entry.person.displayName} place="answerRow" />

            <div className="flex min-w-0 flex-col gap-0.5">
                <span className="text-sm font-semibold">{entry.person.displayName}</span>

                <span className="text-xs text-muted">{entry.relationship}</span>
            </div>

            <div className="ms-auto flex flex-wrap items-center gap-1.5">
                {/* The instant the service sent is what the element carries, and the reader's own wording is what it
                    shows: the two are different things and neither replaces the other.

                    A contact nothing established and a contact whose date did not read are two sentences, because the
                    first is the correspondence being silent and the second is a producer that broke the contract —
                    saying "no contact established" about the second would assert what the run did not say. */}
                {entry.lastContactAt === null || last === null ? (
                    <span className="text-xs whitespace-nowrap text-faint">
                        {translate(
                            entry.lastContactAt === null
                                ? 'answer.peopleLastContactNone'
                                : 'answer.peopleLastContactUnreadable',
                        )}
                    </span>
                ) : (
                    <time className="text-xs whitespace-nowrap text-faint" dateTime={entry.lastContactAt}>
                        {translate('answer.peopleLastContact', { at: last })}
                    </time>
                )}

                {entry.sources.map((source) => (
                    <Citation key={source} position={positionOf(source)} source={source} />
                ))}
            </div>
        </>
    );
}
