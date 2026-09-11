// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useEffect, useId, useState, type RefObject } from 'react';
import {
    mostCitedPassages,
    readCitedPassages,
    type CitedPassage,
    type ClientFailure,
    type ClientFailureReason,
    type ClientSession,
    type MailEnrichmentMark,
    type MailFathomTransport,
} from '@mailfathom/client-backend';
import { Icon } from '../controls/Icon';
import { SecondaryButton } from '../controls/SecondaryButton';
import { SurfaceControl } from '../controls/SurfaceControl';
import type { MessageKey } from '../localization/en';
import { wordInstant } from '../localization/instants';
import { useLocalization } from '../localization/useLocalization';
import { readingNames, readingSources } from './messageReadings';

// Where a reading is checked. Every mark a message carries, each with the sentence, why the producer says it, what
// produced it, and the passages of the message it was drawn from — which is the whole of what makes a mark something
// somebody can disagree with rather than something they have to take.
//
// **A modal rather than something that opens on the row**, which is what the design draws as well. Two things decide
// that here and neither is taste: a row is an `option` of a listbox, which holds no focusable descendant, and its
// height is what the window above it does arithmetic over — so a reading that expanded where it stands would take the
// keyboard path off the list and put every row below it somewhere other than where the list drew the space for it.
//
// **The row's mark opens it under a pointer, and the row's menu is the path everything else takes.** The design puts a
// symbol on the tile between the thread count and the time, and this client draws it there — but hidden from the
// accessibility tree and reachable by pointer alone, for the listbox reason above. What a keyboard, a screen reader and
// a finger held on the row each reach is the menu entry, which was this surface's only affordance before the mark and
// remains the one that carries its name.
//
// **The evidence is fetched rather than carried.** A row publishes passage identifiers because a list page carrying
// the passages themselves would be publishing a body it had no reason to, so the words arrive when somebody asks to
// see them — one request for the whole message's evidence, made when this is opened.
//
// **Nothing here is written down anywhere.** The sentences, the reasons and the passages are mail, and they live for
// as long as this dialog is open.

/** What is being checked: one message's readings, and the message its evidence is followed against. */
export interface AskedReadings {
    readonly storedEmailId: string;
    readonly subject: string | null;
    readonly marks: readonly MailEnrichmentMark[];
}

const failureLabels: Readonly<Record<ClientFailureReason, MessageKey>> = {
    unauthenticated: 'failure.unauthenticated',
    unauthorized: 'failure.unauthorized',
    unavailable: 'failure.unavailable',
    unreadable: 'failure.unreadable',
    missing: 'failure.missing',
};

export function ReadingsAsked({
    asked,
    session,
    transport,
    dialog,
}: {
    /** The readings being checked, or `null` while nothing is. */
    readonly asked: AskedReadings | null;

    readonly session: ClientSession;
    readonly transport: MailFathomTransport;

    /** The dialog itself, opened by whichever surface asked, so that whether it is open is the element's own state. */
    readonly dialog: RefObject<HTMLDialogElement | null>;
}) {
    const { translate } = useLocalization();
    const names = useId();

    return (
        <dialog
            ref={dialog}
            aria-labelledby={names}
            // The whole screen at a phone width and a card above it, which is the design project's own `dlgAi` and the
            // arrangement the settings surface already takes: a card of readings centred in a 390-pixel window leaves
            // a strip of scrim down either side and a body too narrow for the passages the surface exists to show.
            // So the narrow shape is the screen — full size, no border, no radius, no shadow, its own safe-area
            // insets, since a dialog stands in the platform's top layer and the frame's padding is not around it.
            className="m-0 h-full max-h-full w-full max-w-full rounded-none border-0 bg-panel p-0 pt-safe-top pr-safe-right pb-safe-bottom pl-safe-left text-text open:flex open:flex-col backdrop:bg-scrim workspace:m-auto workspace:h-auto workspace:max-h-readings-tall workspace:w-readings workspace:max-w-full workspace:rounded-2xl workspace:border workspace:border-line workspace:shadow-dialog"
        >
            <div className="flex shrink-0 items-center gap-2.5 border-b border-line px-4 py-3">
                <Icon name="auto_awesome" className="size-5 shrink-0 text-accent-strong" />

                <h2 id={names} className="flex-1 truncate text-base font-semibold">
                    {translate('reading.title')}
                </h2>

                <SurfaceControl
                    label={translate('reading.close')}
                    icon="close"
                    onActivate={() => {
                        dialog.current?.close();
                    }}
                />
            </div>

            {/* Keyed by the message, so asking about a second one starts its evidence read rather than reconciling an
                answer with a question the reader has already moved on from. */}
            {asked === null ? null : (
                <AskedMessage key={asked.storedEmailId} asked={asked} session={session} transport={transport} />
            )}
        </dialog>
    );
}

function AskedMessage({
    asked,
    session,
    transport,
}: {
    readonly asked: AskedReadings;
    readonly session: ClientSession;
    readonly transport: MailFathomTransport;
}) {
    const { locale, translate } = useLocalization();
    const evidence = useCitedEvidence(session, transport, asked);

    return (
        // The readings scroll inside whatever height the surface has, which is the whole screen at a phone width and
        // the card's ceiling above it. Neither is a height this states: the dialog is the flex column and this is the
        // one part of it that gives, so a message carrying one reading draws a short card and one carrying three
        // scrolls in a tall one without either being measured here.
        <div className="flex min-h-0 flex-1 flex-col gap-3.5 overflow-y-auto px-4 py-3.5">
            <p className="text-sm text-muted text-pretty">{translate('reading.about')}</p>

            <p className="text-base font-semibold text-pretty">{asked.subject ?? translate('list.noSubject')}</p>

            {asked.marks.map((mark) => {
                const due = mark.dueAt === null ? null : wordInstant(mark.dueAt, locale, 'full');

                return (
                    <section
                        key={mark.aspect}
                        className="flex flex-col gap-2 rounded-xl border border-line bg-sunken px-3.75 py-3.25"
                    >
                        <div className="flex flex-wrap items-center gap-1.5">
                            <h3 className="text-2xs font-semibold tracking-widest text-muted uppercase">
                                {translate(readingNames[mark.aspect])}
                            </h3>

                            {/* What produced the reading, drawn apart from the reading itself and drawn differently
                                for each producer: a deterministic rule re-run over the same message says the same
                                thing and a model promises no such thing, so somebody weighing a mark meets that before
                                they read the sentence. The words carry the distinction as well as the tint, because
                                one only a colour makes is one a reader who cannot see it does not get. */}
                            <span
                                className={`ms-auto rounded-full px-2 py-px text-2xs ${
                                    mark.source === 'Model'
                                        ? 'bg-accent-soft text-accent-deep'
                                        : 'bg-healthy-soft text-healthy-text'
                                }`}
                            >
                                {translate(readingSources[mark.source], { origin: mark.origin })}
                            </span>
                        </div>

                        <p className="text-base text-pretty">{mark.text}</p>

                        {due === null ? null : (
                            <p className="flex items-center gap-1.5 text-sm text-muted">
                                <Icon name="schedule" className="size-4 shrink-0" />

                                {translate('reading.dueAt', { when: due })}
                            </p>
                        )}

                        <p className="text-sm text-muted text-pretty">
                            {translate('reading.reason', { reason: mark.reason })}
                        </p>

                        <MarkEvidence mark={mark} evidence={evidence} />
                    </section>
                );
            })}
        </div>
    );
}

// What one mark rests on, which is the half of this surface somebody who doubts the sentence actually came for. The
// passages are drawn in the order the producer named them, best first, and each says for itself whether the words
// behind it could still be reached.
function MarkEvidence({ mark, evidence }: { readonly mark: MailEnrichmentMark; readonly evidence: CitedEvidence }) {
    const { translate } = useLocalization();

    // A reading with no evidence is one the deployment refuses to store, so this states an absence rather than drawing
    // an empty list under a sentence that is supposed to be checkable.
    if (mark.evidence.length === 0) {
        return <p className="text-sm text-faint">{translate('reading.noEvidence')}</p>;
    }

    if (evidence.reading) {
        return (
            <p className="text-sm text-muted" role="status">
                {translate('reading.readingEvidence')}
            </p>
        );
    }

    if (evidence.failure !== null) {
        return (
            <div className="flex flex-wrap items-center gap-1.5">
                <p className="text-sm text-warning" role="alert">
                    {translate('reading.evidenceFailed', {
                        reason: translate(failureLabels[evidence.failure.reason]),
                    })}
                </p>

                {evidence.failure.reason === 'unavailable' ? (
                    <SecondaryButton label={translate('connection.retry')} onActivate={evidence.readAgain} />
                ) : null}
            </div>
        );
    }

    return (
        <ul className="flex flex-col gap-2">
            {mark.evidence.map((passage) => {
                const cited = evidence.passages.get(passage);
                const words = cited?.text ?? null;

                // Bounded rather than as long as the passage is, which is what the design draws: a card is a summary,
                // and one quotation running the height of the dialog would push every reading after it off the screen.
                return (
                    <li
                        key={passage}
                        className="max-h-37.5 overflow-y-auto rounded-e-lg border-s-2 border-s-line-strong bg-panel px-3 py-2.5 text-sm text-text-soft text-pretty"
                    >
                        {words ?? <span className="text-faint">{translate(insteadOf(cited))}</span>}
                    </li>
                );
            })}
        </ul>
    );
}

/**
 * What is said where a passage produced no words, which is three different things and not one.
 *
 * A passage nothing answered for was never asked about: one request follows ten citations and a message may name
 * twelve, so the last of them are past what this surface follows. Saying the message had moved under it would be
 * telling somebody a fact about their mail that this client has no evidence for.
 */
function insteadOf(cited: CitedPassage | undefined): MessageKey {
    if (cited === undefined) {
        return 'reading.passageNotFollowed';
    }

    return cited.outcome === 'PrivateSource' ? 'reading.passagePrivate' : 'reading.passageGone';
}

interface CitedEvidence {
    readonly reading: boolean;
    readonly failure: ClientFailure | null;
    readonly passages: ReadonlyMap<string, CitedPassage>;
    readonly readAgain: () => void;
}

/** What one answer was to, so an answer that arrives for a question nobody is asking any more is not read as this one. */
interface EvidenceAnswer {
    readonly asking: string;
    readonly attempt: number;
    readonly failure: ClientFailure | null;
    readonly passages: ReadonlyMap<string, CitedPassage>;
}

// One read for the whole message's evidence rather than one per mark: three readings resting on four passages each is
// within the ten one request follows, so a message's readings are backed by a single request however many there are.
//
// Whether it is still reading is derived from whether the answer standing is the answer to the question being asked,
// rather than kept as a second piece of state beside it: the two would have to be held in step, and the pair that
// disagrees is a spinner over an answer that has already arrived.
function useCitedEvidence(session: ClientSession, transport: MailFathomTransport, asked: AskedReadings): CitedEvidence {
    const wanted = [...new Set(asked.marks.flatMap((mark) => mark.evidence))].slice(0, mostCitedPassages);

    // The identities joined rather than the array itself, because a fresh array of the same passages on every render
    // would start the read again on every render. They are identifiers rather than words, so this is a key rather
    // than a copy of anything anybody wrote.
    const asking = wanted.join('\n');

    const [attempt, setAttempt] = useState(0);
    const [answer, setAnswer] = useState<EvidenceAnswer | null>(null);

    const standing = answer !== null && answer.asking === asking && answer.attempt === attempt ? answer : null;

    useEffect(() => {
        if (asking === '') {
            return;
        }

        let listening = true;

        void readCitedPassages(session, transport, asked.storedEmailId, asking.split('\n')).then((result) => {
            if (!listening) {
                return;
            }

            setAnswer({
                asking,
                attempt,
                failure: result.outcome === 'failed' ? result.failure : null,
                passages:
                    result.outcome === 'read'
                        ? new Map(result.value.map((cited) => [cited.passage, cited]))
                        : new Map(),
            });
        });

        return () => {
            listening = false;
        };
    }, [session, transport, asked.storedEmailId, asking, attempt]);

    return {
        reading: asking !== '' && standing === null,
        failure: standing?.failure ?? null,
        passages: standing?.passages ?? new Map(),
        readAgain: () => {
            setAttempt(attempt + 1);
        },
    };
}
