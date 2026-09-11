// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useEffect, useRef, useState, type KeyboardEvent } from 'react';
import { longestDraftInstruction } from '@mailfathom/client-backend';
import type { MessageKey } from '../localization/en';
import { useLocalization } from '../localization/useLocalization';

// Asking the deployment to write the message, as the design project draws it over the body: a field with the product's
// mark on it, the four acts beside it, the three tones after them, and a line saying what the draft is written against
// and where it stays. It is the composer's own block rather than a screen somebody is sent to, which is the whole
// point of it — a draft is a proposal inside the message being written, so nothing here navigates and nothing here
// sends.
//
// **What is typed and what is pressed are two halves of one request.** The field says what the message should be
// about and an act says what shape it should take, so pressing one sends both: the act's own sentence, then whatever
// was typed. Nothing is sent on its own, which is why the field has no submit of its own — a prompt with no act named
// is a request the deployment would have to guess the shape of.
//
// **The acts and the tone travel in English however the screen is read**, and that is deliberate rather than an
// oversight of the two languages: what goes out is an instruction to a writer rather than a sentence anybody reads, and
// the deployment infers the language of the message from the correspondence it answers — so an instruction written in
// the reader's language would be a second, silent answer to a question the correspondence has already settled. What is
// translated is the label on the control, which is what somebody actually reads.

/** One act the block offers, as the design draws it and as the deployment is asked for it. */
interface DraftingAct {
    readonly key: string;
    readonly named: MessageKey;

    /** What the deployment is told the message should be, before whatever its author typed. */
    readonly asks: string;
}

// The design's own four, in the design's own order. Each is a re-drafting rather than an edit of what is on the
// screen — which is what the design's prototype does with all four as well, and what the drafting route offers: it
// writes a message out of the correspondence and the instruction, and reads nothing back out of the composer.
const writeADraft: DraftingAct = { key: 'write', named: 'compose.aiWrite', asks: 'Write this message.' };

const draftingActs: readonly DraftingAct[] = [
    writeADraft,
    { key: 'shorten', named: 'compose.aiShorten', asks: 'Write this message short: the fewest sentences that say it.' },
    { key: 'expand', named: 'compose.aiExpand', asks: 'Write this message in full, spelling out what it rests on.' },
    { key: 'language', named: 'compose.aiFixLanguage', asks: 'Write this message again in careful, correct wording.' },
];

/** The three the design offers, in its own order, with the middle one standing as what nobody chose. */
const tones = [
    { key: 'formal', named: 'compose.aiToneFormal', asks: 'Keep the tone formal.' },
    { key: 'neutral', named: 'compose.aiToneNeutral', asks: null },
    { key: 'direct', named: 'compose.aiToneDirect', asks: 'Keep the tone direct and brief.' },
] as const satisfies readonly { key: string; named: MessageKey; asks: string | null }[];

// Which way each arrow moves the tone in force. A radio group is one tab stop and the arrows are what choose inside
// it, so the role below is a promise about the keyboard rather than a label for the shading — and a promise a reader
// is told about and then finds unkept is worse than never making it.
const toneSteps: Readonly<Record<string, number>> = {
    ArrowLeft: -1,
    ArrowUp: -1,
    ArrowRight: 1,
    ArrowDown: 1,
};

type Tone = (typeof tones)[number]['key'];

export function DraftingBlock({
    context,
    busy,
    asked,
    onDraft,
}: {
    /** What the draft is written against, as the line under the acts names it: the subject, or nothing yet. */
    readonly context: string;

    /** Whether a draft is being written now, which is what keeps one press from asking twice. */
    readonly busy: boolean;

    /**
     * What the composer was opened asking for, which the field opens holding.
     *
     * The bar under a correspondence submits a question, and asking for a draft there opens the composer rather than
     * answering in place — so the words travel here and are still editable, which is what makes the block the one
     * place a drafting is asked for.
     */
    readonly asked: string;

    /** Asks the deployment for a draft, with the act's own sentence and whatever was typed beside it. */
    readonly onDraft: (instruction: string) => void;
}) {
    const { translate } = useLocalization();
    const [written, setWritten] = useState(asked);
    const [tone, setTone] = useState<Tone>('neutral');
    const ranOnce = useRef(false);

    // The drafting the composer was opened asking for. A request going out is what an effect is for, and this one goes
    // out once however often the composer re-renders — the words stay in the field afterwards, so asking again is a
    // press rather than something a render does on somebody's behalf.
    useEffect(() => {
        if (ranOnce.current || asked.trim() === '') {
            return;
        }

        ranOnce.current = true;
        onDraft([writeADraft.asks, asked.trim()].join(' ').slice(0, longestDraftInstruction));
    }, [asked, onDraft]);

    function askFor(act: DraftingAct): void {
        const chosen = tones.find((one) => one.key === tone)?.asks ?? null;

        // Cut here as well as at the wire, because what the deployment refuses is the whole instruction rather than
        // the part somebody typed — and a request refused for length would be refused with a sentence nobody asked to
        // read. The act and the tone come first because they are what the request is; the rest is what was typed.
        const said = [act.asks, ...(chosen === null ? [] : [chosen]), written.trim()]
            .filter((part) => part !== '')
            .join(' ');

        onDraft(said.slice(0, longestDraftInstruction));
    }

    // Moving the tone with the arrows, and taking the focus with it, which is the other half of what the group's role
    // promises. The focused control is found among the group's own children rather than through a ref apiece: the
    // three are rendered from one list, so their order on the screen is the list's order and nothing else decides it.
    function moveTone(event: KeyboardEvent<HTMLButtonElement>): void {
        const step = toneSteps[event.key];

        if (step === undefined) {
            return;
        }

        event.preventDefault();

        const landing = (tones.findIndex((one) => one.key === tone) + step + tones.length) % tones.length;
        const moved = tones[landing];

        if (moved === undefined) {
            return;
        }

        setTone(moved.key);

        const control = event.currentTarget.parentElement?.children[landing];

        if (control instanceof HTMLElement) {
            control.focus();
        }
    }

    return (
        <div className="flex shrink-0 flex-col gap-2.25 border-b border-accent-line bg-accent-soft px-3.75 py-2.75">
            <div className="flex items-center gap-2.75 rounded-xl border-2 border-accent bg-panel px-2.75 py-2 transition focus-within:ring-3 focus-within:ring-accent-soft">
                <span
                    aria-hidden="true"
                    className="shrink-0 rounded-sm bg-accent px-1.75 py-0.75 text-2xs font-semibold tracking-widest text-on-accent"
                >
                    {translate('ai.badge')}
                </span>

                <input
                    aria-label={translate('compose.aiPrompt')}
                    placeholder={translate('compose.aiPromptPlaceholder')}
                    value={written}
                    maxLength={longestDraftInstruction}
                    className="min-w-0 flex-1 bg-transparent text-base text-text outline-none placeholder:text-faint"
                    onChange={(event) => {
                        setWritten(event.target.value);
                    }}
                />
            </div>

            <div className="flex flex-wrap items-center gap-1.75">
                {draftingActs.map((act) => (
                    <button
                        key={act.key}
                        type="button"
                        disabled={busy}
                        className="rounded-full border border-accent-line bg-panel px-3 py-1.5 text-sm whitespace-nowrap text-accent-deep transition hover:bg-accent hover:text-on-accent disabled:opacity-60"
                        onClick={() => {
                            askFor(act);
                        }}
                    >
                        {translate(act.named)}
                    </button>
                ))}

                <span aria-hidden="true" className="mx-0.75 h-5 w-px bg-accent-line" />

                {/* A radio group rather than four more buttons, because one of the three is always in force and a
                    reader who cannot see which is shaded has no other way to be told. */}
                <div role="radiogroup" aria-label={translate('compose.aiTone')} className="flex items-center gap-1.75">
                    {tones.map((one) => (
                        <button
                            key={one.key}
                            type="button"
                            role="radio"
                            aria-checked={tone === one.key}
                            // The group is one tab stop: the tone in force is what a Tab lands on, and the arrows
                            // move within it. Three separate stops would make the keyboard disagree with the role.
                            tabIndex={tone === one.key ? 0 : -1}
                            className={`rounded-full px-2.75 py-1.25 text-sm whitespace-nowrap transition ${
                                tone === one.key
                                    ? 'bg-accent text-on-accent'
                                    : 'border border-line text-text-soft hover:bg-hover'
                            }`}
                            onClick={() => {
                                setTone(one.key);
                            }}
                            onKeyDown={moveTone}
                        >
                            {translate(one.named)}
                        </button>
                    ))}
                </div>
            </div>

            <div className="flex flex-wrap items-center gap-2.25 text-xs text-accent-deep">
                <span className="max-w-full truncate rounded-sm bg-panel px-2 py-0.75">
                    {translate('compose.aiContext', { context })}
                </span>

                <span>{translate('compose.aiStaysLocal')}</span>
            </div>
        </div>
    );
}

/**
 * What became of the last drafting, drawn between the formatting bar and the words the way the design draws it.
 *
 * Two states and neither is an error: one says a draft is being written, and one says the words on the screen are a
 * draft nobody has read yet. The second is what the send confirmation cautions about, which is why accepting it is a
 * control here rather than something that happens when somebody types.
 */
export function DraftingStanding({
    busy,
    drafted,
    onRestore,
    onAccept,
}: {
    readonly busy: boolean;

    /** Whether the words on the screen are a draft its author has neither accepted nor restored away from. */
    readonly drafted: boolean;

    /** Puts back what was written before the draft replaced it. */
    readonly onRestore: () => void;

    /** Takes the draft as the author's own words, which is what stops the send confirmation cautioning about it. */
    readonly onAccept: () => void;
}) {
    const { translate } = useLocalization();

    if (busy) {
        return (
            <p
                role="status"
                className="shrink-0 border-b border-line-soft bg-sunken px-3.75 py-1.75 text-sm text-muted"
            >
                {translate('compose.aiDrafting')}
            </p>
        );
    }

    if (!drafted) {
        return null;
    }

    return (
        <div className="flex shrink-0 flex-wrap items-center gap-3 border-b border-line-soft bg-sunken px-3.75 py-2">
            <p className="text-sm text-text-soft">{translate('compose.aiDraftShown')}</p>

            <button
                type="button"
                className="rounded-md px-1.5 py-1 text-sm text-accent-deep underline transition hover:bg-hover"
                onClick={onRestore}
            >
                {translate('compose.aiRestore')}
            </button>

            <button
                type="button"
                className="rounded-md px-1.5 py-1 text-sm text-muted transition hover:bg-hover hover:text-text"
                onClick={onAccept}
            >
                {translate('compose.aiAccept')}
            </button>
        </div>
    );
}
