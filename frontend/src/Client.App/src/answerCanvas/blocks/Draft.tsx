// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useId, useState } from 'react';
import type { AnswerBlock } from '@mailfathom/client-backend';
import { Icon } from '../../controls/Icon';
import { useLocalization } from '../../localization/useLocalization';
import { AnswerBlockCard, UnrecognisedAnswerBlock } from '../AnswerBlockCard';
import { Citation } from './Citation';
import { draftDispositions, supportVerdicts } from './blockWording';

// Text to be sent, where the answer to a question is a message rather than a fact about one. Everything about how this
// is drawn follows from one property of the contract: a plan carries a draft and never an act, and the set the
// disposition is closed over holds no member meaning sent.
//
// **Nothing sends it, and the card says so where the text is.** The chip above the draft states what has become of it
// locally — composed here, written into the drafts folder, waiting in the outbox — and each of those sentences says that
// nothing left. A reader looking at a message addressed to somebody else is owed that beside the text rather than in a
// legend, and a control that sent it would belong to the surface that governs sending rather than to a block of an
// answer.
//
// **It is editable, and the edit stays on this screen.** The draft is somebody's to correct before they put their name
// to it, so the body opens in a text area on request; saving it anywhere is the mail surface's and is stated under the
// area rather than implied by an absent control. That is what the design project draws too — its own editing state swaps
// the paragraphs for a text area — and the *Send* beside it there is the conversation surface's control, not this one's.

/**
 * Who a draft is addressed to, as one list in the reader's own language.
 *
 * Addresses rather than names, because an address is the whole of what the contract publishes here — and it is also what
 * somebody about to put their name to a message is checking, a name alone being exactly what hides a reply addressed to
 * the wrong one of two people called the same thing. `Intl` joins them, so which separator a language uses and what it
 * puts before the last one are the locale's answers rather than a comma written here.
 */
function addressedTo(recipients: readonly string[], locale: string): string {
    return new Intl.ListFormat(locale).format(recipients);
}

export function Draft({ block }: { readonly block: AnswerBlock }) {
    const { locale, translate } = useLocalization();
    const written = useId();

    // The edit is this block's own state and nothing outside it reads one, which is why it is held here rather than
    // lifted: it is what somebody is typing, not a copy of anything the deployment holds.
    //
    // Two pieces rather than one, because closing the editor and discarding what was typed are two different acts and
    // only one of them was asked for: *Stop editing* puts the text back as a paragraph, and what it says is still what
    // the reader wrote. Nothing here throws away an edit, which is what the sentence under the area promises.
    const [editing, setEditing] = useState(false);
    const [typed, setTyped] = useState<string | null>(null);

    if (block.type !== 'draft') {
        return <UnrecognisedAnswerBlock named={block.named} />;
    }

    const { draft, evidence } = block;
    const verdict = supportVerdicts[evidence.support];
    const body = typed ?? draft.body;

    return (
        <AnswerBlockCard label={translate('draft.label')} state="ready">
            <div className="flex flex-wrap items-center gap-2">
                <span className="rounded-full bg-rail px-2.25 py-0.5 text-2xs text-muted">
                    {translate(draftDispositions[draft.disposition])}
                </span>

                <span className={`flex items-center gap-1 rounded-full px-2.25 py-0.5 text-2xs ${verdict.tint}`}>
                    <Icon className="size-3.25" name={verdict.icon} />
                    {translate(verdict.label)}
                </span>
            </div>

            <div className="flex flex-col gap-2 rounded-lg border border-line bg-sunken px-3.75 py-3.25">
                <p className="flex flex-wrap gap-2 text-sm">
                    <span className="text-muted">{translate('draft.to')}</span>

                    <span>{addressedTo(draft.recipients, locale)}</span>
                </p>

                <p className="flex flex-wrap gap-2 text-sm">
                    <span className="text-muted">{translate('draft.subject')}</span>

                    <span className="font-semibold">{draft.subject}</span>
                </p>

                {editing ? (
                    <>
                        <label className="text-2xs tracking-wider text-muted uppercase" htmlFor={written}>
                            {translate('draft.bodyLabel')}
                        </label>

                        {/* Focus goes where it was asked for: the control that opened the area is after it in the
                            document, so a reader who did not get the caret would have to come back past every citation
                            the draft carries. */}
                        <textarea
                            className="min-h-32 w-full rounded-md border border-line bg-panel px-2.75 py-2 text-sm leading-relaxed focus-visible:border-accent"
                            id={written}
                            autoFocus
                            value={body}
                            onChange={(event) => {
                                setTyped(event.target.value);
                            }}
                        />
                    </>
                ) : (
                    <p className="text-sm leading-relaxed whitespace-pre-line text-text-soft text-pretty">{body}</p>
                )}

                {/* The sentence stays with the text it is about rather than with the text area: once the editor closes,
                    the paragraph above draws what the reader wrote under a chip saying what became of the draft, and
                    nothing else would say that the text on the screen is not the text the deployment holds. */}
                {editing || typed !== null ? (
                    <p className="text-xs text-faint text-pretty">{translate('draft.editKept')}</p>
                ) : null}

                {evidence.citations.length === 0 ? null : (
                    <p className="flex flex-wrap items-center gap-1.5">
                        {evidence.citations.map((source, at) => (
                            <Citation key={source} position={at + 1} source={source} />
                        ))}
                    </p>
                )}
            </div>

            <button
                className="self-start rounded-md border border-line-strong px-3 py-1.75 text-sm text-text-soft transition hover:bg-hover"
                type="button"
                onClick={() => {
                    setEditing(!editing);
                }}
            >
                {translate(editing ? 'draft.stopEditing' : 'draft.edit')}
            </button>
        </AnswerBlockCard>
    );
}
