// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useEffect, useId, useRef, useState, type KeyboardEvent, type ReactNode } from 'react';
import {
    readMailMessage,
    type ClientFailureReason,
    type ClientSession,
    type MailAccount,
    type MailDraftAnswer,
    type MailFathomTransport,
    type MailStagedAttachment,
} from '@mailfathom/client-backend';
import { Icon } from '../controls/Icon';
import type { MessageKey } from '../localization/en';
import type { Locale } from '../localization/locale';
import { sizeOf } from '../localization/octets';
import { useLocalization } from '../localization/useLocalization';
import { useWideWorkspace } from '../shell/useWideWorkspace';
import { anythingWritten, answerTo, nothingWrittenYet, type ComposerOpening, type Composition } from './composition';
import { DiscardConfirmation } from './DiscardConfirmation';
import { forgetComposition, rememberComposition, rememberedComposition } from './keptComposition';
import { RecipientField } from './RecipientField';
import { SendConfirmation } from './SendConfirmation';
import { useDraftAtDeployment, type DraftStanding } from './useDraftAtDeployment';

// Writing a message, as the design project composes it: one model in two shapes, decided by the width the client has
// rather than by which head it runs on. Wide, it is the reading column — what is being written stands where what is
// being read stands, with the mailboxes and the list still beside it, so a reply is written against a conversation one
// press away rather than behind a window. Narrow, it is the screen, because a column that has to hold a header, four
// fields, and a footer has nothing left over to show a message underneath.
//
// **The body is plain text, and this is where that decision shows.** What the client surface takes is a plain-text
// draft with an optional HTML alternative, and rich authoring is out of this stage's scope, so what is drawn is the
// platform's own multi-line field rather than an editable region and a row of formatting controls. The design draws
// that row, and the block of AI actions above it; both belong to the stage that adds them.
//
// **An answer's subject is read-only, and that is the platform rather than a choice.** A save either names an account
// and a subject, or names the message it answers and lets the deployment derive both — so an edited subject on a reply
// is a value the surface has nowhere to put, and offering the field would be offering an edit that is discarded.

/** What the composer is doing before there is anything to write in, which is a state of its own rather than a blank. */
type Reading = { readonly kind: 'reading' } | { readonly kind: 'unread'; readonly reason: ClientFailureReason };

// What each of the four failures means for somebody trying to write mail, said as what they do next. The reading
// failure and the deployment's own are worded the same way because they are the same four answers.
const failureSaid: Readonly<Record<ClientFailureReason, MessageKey>> = {
    unauthenticated: 'compose.failedUnauthenticated',
    unauthorized: 'compose.failedUnauthorized',
    unavailable: 'compose.failedUnavailable',
    unreadable: 'compose.failedUnreadable',
};

// What each rule that refuses a send is called, and what would change it. Exhaustive by its own type, so a refusal the
// surface adds fails to compile until somebody has written what a person does about it.
const refusalSaid = {
    sendingNotEnabled: 'compose.refusedSendingNotEnabled',
    recipientRefused: 'compose.refusedRecipient',
    ceilingReached: 'compose.refusedCeiling',
    contentRefused: 'compose.refusedContent',
    notFullyScanned: 'compose.refusedNotScanned',
    screeningUnavailable: 'compose.refusedScreeningUnavailable',
    refusedForAnotherReason: 'compose.refusedForAnotherReason',
} as const satisfies Readonly<Record<string, MessageKey>>;

// What became of a send somebody took back, which is four answers rather than a success and a failure: a message
// already going out cannot be recalled, and saying so is the answer.
const withdrawalSaid = {
    withdrawn: 'compose.withdrawn',
    alreadyBeingSent: 'compose.alreadyBeingSent',
    pastRecall: 'compose.pastRecall',
    noSuchSend: 'compose.noSuchSend',
} as const satisfies Readonly<Record<string, MessageKey>>;

// Everything a keyboard may land on, as the platform decides it rather than as a list of the composer's own
// controls: a control added later is caught by this without anybody remembering to name it here.
const reachableControls =
    'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]';

const titles: Readonly<Record<'new' | MailDraftAnswer, MessageKey>> = {
    new: 'compose.titleNew',
    senderOnly: 'compose.titleReply',
    everyone: 'compose.titleReplyAll',
    forward: 'compose.titleForward',
};

export function Composer({
    session,
    transport,
    accounts,
    opening,
    online,
    onClosed,
}: {
    readonly session: ClientSession;
    readonly transport: MailFathomTransport;

    /** The mailboxes a message may go out from, which is what a message of its own is addressed from. */
    readonly accounts: readonly MailAccount[];

    readonly opening: ComposerOpening;
    readonly online: boolean;
    readonly onClosed: () => void;
}) {
    const { locale, translate } = useLocalization();
    const wide = useWideWorkspace();
    const draft = useDraftAtDeployment(session, transport);
    const [composition, setComposition] = useState(() => opened(opening, accounts));
    const [reading, setReading] = useState<Reading>({ kind: 'reading' });
    const [known, setKnown] = useState<readonly string[]>([]);
    const [copiesShown, setCopiesShown] = useState(false);
    const files = useRef<HTMLInputElement>(null);
    const asked = useRef<HTMLDialogElement>(null);
    const frame = useRef<HTMLElement>(null);
    const subjectId = useId();
    const wordsId = useId();

    // An answer opens addressed to the people in the conversation, which means reading the message it answers. A
    // request going out is what an effect is for; the answer is discarded where the composer stopped listening for it,
    // and a message already restored from what this tab was writing needs no read at all.
    useEffect(() => {
        if (opening.kind !== 'answer') {
            return;
        }

        let listening = true;

        void readMailMessage(session, transport, opening.storedEmailId).then((answer) => {
            if (!listening) {
                return;
            }

            if (answer.outcome === 'failed') {
                setReading({ kind: 'unread', reason: answer.failure.reason });

                return;
            }

            setKnown(answer.value.headers.participants.map((participant) => participant.address));
            setComposition((held) => held ?? answerTo(answer.value, opening.answers));
        });

        return () => {
            listening = false;
        };
    }, [session, transport, opening]);

    // The local draft, kept continuously so that a reload returns to what was being written rather than to nothing.
    // A browser store is something outside React, which is what an effect synchronizes with.
    useEffect(() => {
        if (composition !== null) {
            rememberComposition(composition);
        }
    }, [composition]);

    // Opening the composer is a view change, so focus goes into it rather than staying on the control that opened it.
    // Once, as the fields appear, on the first one there is to write in — the words for an answer, whose recipients are
    // written already, and the recipients for a message of its own.
    const written = composition !== null;

    useEffect(() => {
        if (written) {
            frame.current?.querySelector<HTMLElement>(opening.kind === 'answer' ? 'textarea' : 'input')?.focus();
        }
    }, [written, opening.kind]);

    function revise(change: Partial<Composition>): void {
        setComposition((held) => (held === null ? null : { ...held, ...change }));
    }

    // Tab kept inside the composer while it stands over the whole screen. The two confirmations are `<dialog>`
    // elements and hold the keyboard themselves once open, so what is inside a closed one is skipped rather than
    // counted — a closed dialog draws nothing and is not somewhere a keyboard may land.
    function holdTheKeyboard(event: KeyboardEvent<HTMLElement>): void {
        if (wide || event.key !== 'Tab' || frame.current === null) {
            return;
        }

        const reachable = [...frame.current.querySelectorAll<HTMLElement>(reachableControls)].filter(
            (control) => control.tabIndex !== -1 && control.closest('dialog:not([open])') === null,
        );

        const first = reachable.at(0);
        const last = reachable.at(-1);

        if (first === undefined || last === undefined) {
            return;
        }

        if (event.shiftKey && document.activeElement === first) {
            event.preventDefault();
            last.focus();
        } else if (!event.shiftKey && document.activeElement === last) {
            event.preventDefault();
            first.focus();
        }
    }

    // One file at a time. The draft this stages against is written by whichever save answers first, so two uploads
    // started together would each file a draft of their own and every file but the last would be staged against one
    // nothing sends.
    async function attachInTurn(chosen: readonly File[]): Promise<void> {
        if (composition === null) {
            return;
        }

        for (const file of chosen) {
            await draft.attach(composition, file);
        }
    }

    function close(): void {
        forgetComposition();
        onClosed();
    }

    // Whether a send may start at all, read by the button and by the keyboard shortcut alike: two ways to ask are
    // one act, and a second press while the first is in flight would queue the same message twice.
    const sendable = online && draft.standing.kind !== 'sending';

    const title = translate(titles[opening.kind === 'new' ? 'new' : opening.answers]);

    return (
        <section
            ref={frame}
            aria-label={title}
            // Narrow, this covers the whole viewport, which makes it a dialog whatever it is composed out of: the
            // spaces beside it stay mounted underneath, so without this a keyboard would tab straight off the screen
            // into controls nobody can see. Wide it is a column beside the others and neither is true.
            role={wide ? undefined : 'dialog'}
            aria-modal={wide ? undefined : true}
            onKeyDown={holdTheKeyboard}
            className={`flex flex-col bg-panel text-text ${
                wide ? 'h-full min-h-0' : 'fixed inset-0 z-50 pt-safe-top pb-safe-bottom'
            }`}
        >
            <div className="flex shrink-0 items-center gap-3 border-b border-line px-4.25 py-3">
                <h2 className="text-md font-semibold">{title}</h2>

                <div className="ms-auto flex items-center">
                    <DiscardConfirmation
                        written={composition !== null && (anythingWritten(composition) || draft.staged.length > 0)}
                        onDiscard={() => {
                            void draft.discard();
                            close();
                        }}
                        onKeep={() => {
                            if (composition !== null) {
                                // A draft the deployment refused to file is one the composer stays open on, because
                                // closing on it is how what somebody wrote is lost quietly.
                                void draft.save(composition).then((filed) => {
                                    if (filed) {
                                        close();
                                    }
                                });
                            }
                        }}
                    />
                </div>
            </div>

            {composition === null ? (
                <BeforeAnythingIsWritten reading={reading} />
            ) : (
                <>
                    {accounts.length > 1 && composition.answering === null ? (
                        <div className="flex items-center gap-2.5 border-b border-line-soft px-4.25 py-2.25">
                            <label htmlFor={`${subjectId}-from`} className="w-11 shrink-0 text-sm text-muted">
                                {translate('compose.from')}
                            </label>

                            <select
                                id={`${subjectId}-from`}
                                value={composition.account}
                                className="flex-1 rounded-md border border-line bg-panel px-2 py-1 text-base text-text"
                                onChange={(event) => {
                                    revise({ account: event.target.value });
                                }}
                            >
                                {accounts.map((account) => (
                                    <option key={account.id} value={account.id}>
                                        {account.displayName}
                                    </option>
                                ))}
                            </select>
                        </div>
                    ) : null}

                    <RecipientField
                        label={translate('compose.to')}
                        addresses={composition.to}
                        completions={known}
                        onChanged={(to) => {
                            revise({ to });
                        }}
                        trailing={
                            copiesShown ? null : (
                                <button
                                    type="button"
                                    aria-label={translate('compose.showCopies')}
                                    className="shrink-0 rounded-md px-1.5 py-1 text-sm text-muted transition hover:bg-hover hover:text-text"
                                    onClick={() => {
                                        setCopiesShown(true);
                                    }}
                                >
                                    {translate('compose.copyHeaders')}
                                </button>
                            )
                        }
                    />

                    {copiesShown ? (
                        <>
                            <RecipientField
                                label={translate('compose.cc')}
                                addresses={composition.cc}
                                completions={known}
                                onChanged={(cc) => {
                                    revise({ cc });
                                }}
                            />

                            <RecipientField
                                label={translate('compose.bcc')}
                                addresses={composition.bcc}
                                completions={known}
                                onChanged={(bcc) => {
                                    revise({ bcc });
                                }}
                            />
                        </>
                    ) : null}

                    <div className="flex items-center gap-2.5 border-b border-line px-4.25 py-2.75">
                        <label htmlFor={subjectId} className="w-11 shrink-0 text-sm text-muted">
                            {translate('compose.subject')}
                        </label>

                        {composition.answering === null ? (
                            <input
                                id={subjectId}
                                value={composition.subject}
                                placeholder={translate('compose.subjectPlaceholder')}
                                className="min-w-0 flex-1 border-none bg-transparent text-md text-text outline-none placeholder:text-faint"
                                onChange={(event) => {
                                    revise({ subject: event.target.value });
                                }}
                            />
                        ) : (
                            <p id={subjectId} className="min-w-0 flex-1 truncate text-md">
                                {composition.subject}
                                <span className="sr-only"> {translate('compose.subjectOfAnAnswer')}</span>
                            </p>
                        )}
                    </div>

                    <label htmlFor={wordsId} className="sr-only">
                        {translate('compose.words')}
                    </label>

                    <textarea
                        id={wordsId}
                        value={composition.words}
                        placeholder={translate('compose.wordsPlaceholder')}
                        className="min-h-40 flex-1 resize-none border-none bg-transparent px-4.25 py-4 text-lg text-text-soft outline-none placeholder:text-faint"
                        onChange={(event) => {
                            revise({ words: event.target.value });
                        }}
                        onKeyDown={(event) => {
                            // The design's own shortcut, and it opens the confirmation rather than sending: what a
                            // keyboard saves is reaching for the control, never the reading of who the message is for.
                            if (event.key === 'Enter' && (event.ctrlKey || event.metaKey) && sendable) {
                                event.preventDefault();
                                asked.current?.showModal();
                            }
                        }}
                    />

                    <StagedFiles
                        staged={draft.staged}
                        locale={locale}
                        onUnstage={(attachmentId) => {
                            void draft.unstage(attachmentId);
                        }}
                    />

                    <WhatIsHappening standing={draft.standing} online={online} onWithdraw={draft.withdraw} />

                    <div className="flex shrink-0 flex-wrap items-center gap-2 border-t border-line px-4.25 py-3">
                        <SendConfirmation
                            asked={asked}
                            composition={composition}
                            disabled={!sendable}
                            onSend={() => {
                                void draft.send(composition);
                            }}
                        />

                        <button
                            type="button"
                            className="flex items-center gap-1.75 rounded-lg border border-line-strong px-3 py-2 text-sm text-text-soft transition hover:bg-hover"
                            onClick={() => {
                                files.current?.click();
                            }}
                        >
                            <Icon name="attach_file" className="size-4.5" />
                            {translate('compose.attach')}
                        </button>

                        {/* The platform's own file picker, kept out of the accessible tree because the control above is
                            what carries the name and the keyboard path. */}
                        <input
                            ref={files}
                            type="file"
                            multiple
                            tabIndex={-1}
                            aria-hidden="true"
                            className="hidden"
                            onChange={(event) => {
                                const chosen = [...(event.target.files ?? [])];

                                event.target.value = '';
                                void attachInTurn(chosen);
                            }}
                        />

                        <button
                            type="button"
                            className="rounded-lg px-2 py-2 text-sm text-muted transition hover:bg-hover hover:text-text"
                            onClick={() => {
                                void draft.save(composition);
                            }}
                        >
                            {translate('compose.saveDraft')}
                        </button>

                        <p className="ms-auto text-xs text-faint">{translate('compose.shortcutSends')}</p>
                    </div>
                </>
            )}
        </section>
    );
}

// Where a composition starts: what this tab was already writing where it matches what is being opened, an empty
// message of its own, or nothing at all while the message an answer is written against is still being read.
function opened(opening: ComposerOpening, accounts: readonly MailAccount[]): Composition | null {
    const kept = rememberedComposition();

    if (kept !== null && sameOpening(kept, opening)) {
        return kept;
    }

    return opening.kind === 'new' ? nothingWrittenYet(accounts[0]?.id ?? '') : null;
}

function sameOpening(kept: Composition, opening: ComposerOpening): boolean {
    return opening.kind === 'new'
        ? kept.answering === null
        : kept.answering?.storedEmailId === opening.storedEmailId && kept.answering.answers === opening.answers;
}

// The composer before there is a message in it, which happens only for an answer: the conversation it is written
// against has to be read before it can be addressed. Both states say what is happening, and the way out of either is
// the control the header already carries — with nothing written, it closes rather than asking.
function BeforeAnythingIsWritten({ reading }: { readonly reading: Reading }) {
    const { translate } = useLocalization();

    return (
        <div className="flex flex-1 flex-col items-start gap-3 px-4.25 py-6">
            <p aria-live="polite" className="text-base text-muted text-pretty">
                {translate(reading.kind === 'reading' ? 'compose.reading' : failureSaid[reading.reason])}
            </p>
        </div>
    );
}

// The files staged against the draft, drawn as the reading pane draws the files a message carries: what each one is
// called and how large it is, before anything is fetched or sent.
function StagedFiles({
    staged,
    locale,
    onUnstage,
}: {
    readonly staged: readonly MailStagedAttachment[];
    readonly locale: Locale;
    readonly onUnstage: (attachmentId: string) => void;
}) {
    const { translate } = useLocalization();

    if (staged.length === 0) {
        return null;
    }

    return (
        <ul
            aria-label={translate('compose.attachedFiles')}
            className="flex shrink-0 flex-wrap gap-2 border-t border-line-soft px-4.25 py-2.5"
        >
            {staged.map((file) => (
                <li
                    key={file.attachmentId}
                    className="flex items-center gap-2 rounded-xl border border-line bg-rail px-2.5 py-1.25 text-base"
                >
                    <Icon name="attach_file" className="size-4 text-muted" />
                    <span className="max-w-60 truncate">{file.fileName}</span>
                    <span className="text-sm text-faint">{sizeOf(file.sizeOctets, locale)}</span>

                    <button
                        type="button"
                        aria-label={translate('compose.removeFile', { name: file.fileName })}
                        className="flex items-center rounded-xs text-faint transition hover:text-text"
                        onClick={() => {
                            onUnstage(file.attachmentId);
                        }}
                    >
                        <Icon name="close" className="size-3.5" />
                    </button>
                </li>
            ))}
        </ul>
    );
}

// What the deployment is doing about the message, said where it happens rather than as a banner over the whole client:
// saving, attaching, queueing, what refused it, and what became of one taken back. Nothing waits in silence, and every
// refusal names what would change it.
function WhatIsHappening({
    standing,
    online,
    onWithdraw,
}: {
    readonly standing: DraftStanding;
    readonly online: boolean;
    readonly onWithdraw: () => Promise<void>;
}) {
    const { translate } = useLocalization();

    if (!online) {
        return <Said text={translate('compose.offline')} warning />;
    }

    switch (standing.kind) {
        case 'held':
            return null;
        case 'saving':
            return <Said text={translate('compose.saving')} />;
        case 'saved':
            return <Said text={translate('compose.saved')} />;
        case 'attaching':
            return <Said text={translate('compose.attaching', { name: standing.fileName })} />;
        case 'sending':
            return <Said text={translate('compose.sending')} />;
        case 'refused':
            return <Said text={translate(refusalSaid[standing.refusal])} warning />;
        case 'failed':
            return <Said text={translate(failureSaid[standing.reason])} warning />;
        case 'withdrawn':
            return <Said text={translate(withdrawalSaid[standing.withdrawal])} />;
        case 'queued':
            return (
                <Said text={translate('compose.queued')}>
                    <button
                        type="button"
                        className="rounded-md px-2 py-1 text-sm font-semibold text-accent-deep underline transition hover:bg-hover"
                        onClick={() => {
                            void onWithdraw();
                        }}
                    >
                        {translate('compose.withdraw')}
                    </button>
                </Said>
            );
    }
}

function Said({
    text,
    warning = false,
    children,
}: {
    readonly text: string;
    readonly warning?: boolean;
    readonly children?: ReactNode;
}) {
    return (
        <p
            aria-live="polite"
            className={`flex shrink-0 flex-wrap items-center gap-2 border-t px-4.25 py-2 text-base text-pretty ${
                warning ? 'border-warning bg-warning-soft text-warning-text' : 'border-line-soft bg-sunken text-muted'
            }`}
        >
            {text}
            {children}
        </p>
    );
}
