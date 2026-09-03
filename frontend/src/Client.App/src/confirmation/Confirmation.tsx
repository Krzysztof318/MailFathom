// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useId, type ReactNode, type RefObject } from 'react';
import { Icon } from '../controls/Icon';
import type { IconName } from '../controls/icons';
import type { MessageKey } from '../localization/en';
import { useLocalization } from '../localization/useLocalization';

// The one question this client puts in front of an act that leaves the deployment, and the vocabulary every screen asks
// it in. Sending, discarding what somebody wrote, closing everything at once, and — from the stage that performs them —
// flagging, filing, archiving, moving, and deleting mail each arrive here rather than each drawing a dialog of its own.
//
// **What it exists to refuse is the generic question.** *Are you sure?* teaches a reader to press yes without reading,
// which is worse than asking nothing at all, because it manufactures consent nobody gave. So nothing here writes a
// sentence: the question, what will change, and what every way out is called are the caller's words, in the terms of
// the thing being changed — *move 4 messages from Inbox to Archive on the work account*, never *this item*. A batch
// says its count and its destination for the same reason, and it says them in the caller's own sentence rather than in
// a shape stated here, because four hundred flags and four hundred moves are not one sentence with a noun swapped.
//
// **What it does state is what happens afterwards**, because that is the half a caller forgets and the half that
// decides how heavy the question should have been. {@link Reversal} is a closed union with no default: a reversible act
// names the period it can be taken back in, an irreversible one says in its own words what it costs, and there is no
// third answer meaning *nobody thought about it*. Offering a way back from something that has none is the same failure
// as *Are you sure?* wearing different clothes.
//
// **What it states about afterwards is prose rather than a row of its own**, which is how the design project words it:
// *we move it to the trash — it leaves the trash after 30 days* is one sentence somebody reads, and lifting the second
// half of it into a labelled line under an icon would turn the thing this exists to make legible into chrome.
//
// **The dialog is the platform's own**, so the page behind it is inert, focus moves into it and is held there, Escape
// leaves it, and leaving it puts focus back on the control that opened it — four obligations none of which is written
// here. Whether it is open is therefore the element's state rather than a second copy of it, which is why the caller
// hands over the reference and draws its own control: two ways to ask are still one question.

/**
 * What somebody can do about an act once it has happened, which the confirmation states before it does.
 *
 * The three are different promises rather than three shades of one, and conflating any two is a promise the client
 * cannot keep: `undoable` names a period this client knows and offers a way back inside, `recallable` says it can be
 * taken back without naming how long, because the deployment decides that and not the screen, and `permanent` offers
 * nothing and says what that costs.
 */
export type Reversal =
    /** Taken back for a period this client knows, from the message that reports the act. */
    | { readonly kind: 'undoable'; readonly forSeconds: number }

    /** Taken back for as long as the deployment allows, which is what `said` states in the act's own terms. */
    | { readonly kind: 'recallable'; readonly said: string }

    /** Not taken back at all, with `said` stating what that costs rather than repeating that it cannot be undone. */
    | { readonly kind: 'permanent'; readonly said: string };

/**
 * How a way out is drawn, which follows what pressing it costs rather than where it sits in the row.
 *
 * `back` leaves the act undone, `act` is the thing being confirmed, and `aside` is a second way out that gives
 * something up without being the act — discarding what was written rather than filing it is the one today.
 */
export type Manner = 'back' | 'aside' | 'act';

/** One way out of a confirmation: what it is called, how it is drawn, and what pressing it does. */
export interface WayOut {
    /** Its label, which names what pressing it does — never `OK`, never `Yes`, and never `Confirm`. */
    readonly said: string;

    readonly manner: Manner;

    /**
     * What pressing it does, run once the dialog has closed and the platform has put focus back where it was.
     *
     * Absent where the way out is simply leaving, which is what `back` usually is.
     */
    readonly run?: () => void;
}

// How each manner is drawn. Exhaustive by its own type, so a manner added to the union fails to compile until it has
// been decided what it looks like.
const mannerDrawn: Readonly<Record<Manner, string>> = {
    back: 'rounded-lg border border-line bg-sunken px-3.75 py-2 text-base text-text-soft transition hover:bg-hover',
    aside: 'rounded-lg border border-warning px-3.75 py-2 text-base text-warning-text transition hover:bg-warning-soft',
    act: 'rounded-lg bg-accent px-4 py-2 text-base font-semibold text-on-accent transition hover:opacity-90',
};

// How long a reversible act stands, in the form the count actually takes. Selected rather than spelled for the reason
// `mailSpace/TabStrip.tsx` gives about counting tabs: Polish needs three forms for this noun and English hides that it
// needs two, so one entry could express neither.
const standsFor: Readonly<Record<Intl.LDMLPluralRule, MessageKey>> = {
    zero: 'confirm.undoableFor.other',
    one: 'confirm.undoableFor.one',
    two: 'confirm.undoableFor.other',
    few: 'confirm.undoableFor.few',
    many: 'confirm.undoableFor.many',
    other: 'confirm.undoableFor.other',
};

export function Confirmation({
    asked,
    mark,
    question,
    consequence,
    cautions = [],
    reversal,
    ways,
}: {
    /**
     * The dialog itself, held by the caller so that every way of asking reaches the same question.
     *
     * The element is the state, which is what the note above is about: neither this component nor the screen above it
     * holds a second copy of whether the question is open.
     */
    readonly asked: RefObject<HTMLDialogElement | null>;

    /** The act's own symbol, drawn beside the question as the design project draws it. */
    readonly mark: IconName;

    /** The question, naming the act rather than asking whether to proceed. */
    readonly question: string;

    /** What will change, in the terms of the thing being changed, as the caller words it. */
    readonly consequence: ReactNode;

    /** What the act would happen without, each said as a sentence. None of them refuses it. */
    readonly cautions?: readonly string[];

    readonly reversal: Reversal;

    /** Every way out, in the order somebody reads them, with the act itself last. */
    readonly ways: readonly WayOut[];
}) {
    const { locale, translate } = useLocalization();
    const asks = useId();
    const explains = useId();

    // Which way out was pressed travels the way the platform carries it, in the return value, so that closing by any
    // route — a press, Escape, the backdrop — arrives in one place with focus already restored.
    function chosen(answered: string): WayOut | undefined {
        // Escape and `close()` both answer with nothing, which is the one answer that is not a way out — and it has to
        // be told apart before the number is read, `Number('')` being zero and zero being the first way in the row.
        return answered === '' ? undefined : ways[Number(answered)];
    }

    return (
        <dialog
            ref={asked}
            aria-labelledby={asks}
            aria-describedby={explains}
            className="m-auto w-110 max-w-full rounded-2xl border border-line bg-panel p-5 text-text shadow-dialog backdrop:bg-scrim"
            onClose={(closing) => {
                const dialog = closing.currentTarget;
                const way = chosen(dialog.returnValue);

                // Emptied rather than left, because a return value outlives the dialog it was set on and not every
                // engine clears it on the next `showModal`: an answer read twice would perform the act again.
                dialog.returnValue = '';

                way?.run?.();
            }}
        >
            <div className="flex flex-col gap-3.5">
                <h2 id={asks} className="flex items-center gap-2.5 text-xl font-semibold">
                    <Icon name={mark} className="size-5 shrink-0 text-accent-strong" />
                    {question}
                </h2>

                <div id={explains} className="flex flex-col gap-1">
                    {consequence}

                    <p className="text-base text-muted text-pretty">
                        {reversal.kind === 'undoable'
                            ? translate(standsFor[new Intl.PluralRules(locale).select(reversal.forSeconds)], {
                                  count: new Intl.NumberFormat(locale).format(reversal.forSeconds),
                              })
                            : reversal.said}
                    </p>
                </div>

                {cautions.length === 0 ? null : (
                    <ul className="flex flex-col gap-1.75 rounded-2xl border border-warning bg-warning-soft px-3.25 py-2.75">
                        {cautions.map((caution) => (
                            <li key={caution} className="flex items-start gap-2 text-base text-warning-text">
                                <Icon name="warning" className="mt-0.5 size-4 shrink-0" />
                                {caution}
                            </li>
                        ))}
                    </ul>
                )}

                <div className="flex flex-wrap justify-end gap-2">
                    {ways.map((way, pressed) => (
                        <button
                            key={way.said}
                            type="button"
                            className={mannerDrawn[way.manner]}
                            onClick={() => {
                                asked.current?.close(String(pressed));
                            }}
                        >
                            {way.said}
                        </button>
                    ))}
                </div>
            </div>
        </dialog>
    );
}
