// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useEffect, useRef, useState } from 'react';
import type { MailThreadMessage, MailThreadState, MailThreadStateAspect } from '@mailfathom/client-backend';
import { Icon } from '../controls/Icon';
import type { MessageKey } from '../localization/en';
import { wordInstant } from '../localization/instants';
import { useLocalization } from '../localization/useLocalization';
import { useScreenLayer } from '../shell/screenLayers';
import { useDesktopComposition, useWideWorkspace } from '../shell/useWideWorkspace';
import { sourceOf, type ThreadStateSource } from './threadStateSources';

// Where a conversation stands, drawn beside the conversation itself: what its people settled, what they raised and
// left open, what anybody undertook, and how a document they exchanged changed between two versions of it. Each
// statement carries the message it rests on, and following one reveals that message in the conversation below rather
// than opening anything of its own.
//
// The design project draws it three ways and the composition decides which, from the width alone:
//
// - **Desktop** — a row of cards above the conversation, each carrying its label, what it says, and its source.
// - **Tablet and the fold** — the same statements as chips inside the body, tighter and without the source link,
//   because a card that has to hold a link as well stops fitting at that width.
// - **A phone** — one line saying the first of them, and a control opening the whole block as a sheet.
//
// Absence is a state rather than a failure, and it is the common one: a deployment that never turned the derivation
// on and one that has not reached this conversation yet both answer with nothing at all. So a conversation with no
// state says so in a sentence and keeps its own chrome, rather than drawing an error somebody would try to act on.

const aspectLabels: Readonly<Record<MailThreadStateAspect, MessageKey>> = {
    Agreement: 'threadState.agreement',
    OpenQuestion: 'threadState.openQuestion',
    Commitment: 'threadState.commitment',
    VersionDifference: 'threadState.versionDifference',
};

function nothingDrawn(reading: boolean, state: MailThreadState | null): MessageKey {
    if (reading) {
        return 'threadState.reading';
    }

    return state?.coverage === 'ThreadTooLarge' ? 'threadState.tooLarge' : 'threadState.none';
}

export function ThreadState({
    state,
    reading,
    messages,
    onFollowSource,
}: {
    /** Where the conversation stands, or `null` where this deployment has derived nothing about it. */
    readonly state: MailThreadState | null;

    /** Whether the block is still being read, which is what the surface says instead of looking finished. */
    readonly reading: boolean;

    /** The conversation's messages, which is what a statement's source is resolved against. */
    readonly messages: readonly MailThreadMessage[];

    /** Reveals the message a statement rests on, in the conversation below. */
    readonly onFollowSource: (storedEmailId: string) => void;
}) {
    const { translate } = useLocalization();
    const wideWorkspace = useWideWorkspace();
    const desktop = useDesktopComposition();

    const entries = state?.entries ?? [];

    // Nothing to draw is three different sentences and one shape: a block still being read, a conversation too long
    // for a state to be derived from in one go, and a conversation nothing has been derived about at all.
    if (entries.length === 0) {
        return (
            <section
                aria-label={translate('threadState.label')}
                className="border-b border-line bg-sunken px-5.5 py-2.25"
            >
                <p role="status" className="text-sm text-muted">
                    {translate(nothingDrawn(reading, state))}
                </p>
            </section>
        );
    }

    if (!wideWorkspace) {
        return <StateLine entries={entries} messages={messages} onFollowSource={onFollowSource} />;
    }

    // The two wide compositions draw the same statements and differ in what a card has room for, which is why the
    // source link is the one thing that goes: at the tablet's width a chip holds a label and a line, and the block
    // stops being a glance the moment it holds three lines each.
    return (
        <section
            aria-label={translate('threadState.label')}
            className={desktop ? 'flex flex-col gap-1.5 border-b border-line bg-sunken px-5.5 py-2.25' : 'px-5.5 pt-3'}
        >
            <ul
                className={
                    desktop
                        ? 'flex flex-wrap gap-2'
                        : 'flex flex-wrap gap-1.75 rounded-lg border border-line bg-sunken p-2'
                }
            >
                {entries.map((entry, place) => (
                    <li
                        // A statement carries no identity of its own, and two of one aspect may say related things, so
                        // the place it holds is what keys it — the block is replaced whole whenever it is derived
                        // again rather than reordered under a reader.
                        key={`${entry.aspect}-${String(place)}`}
                        className={
                            desktop
                                ? 'flex min-w-0 flex-1 basis-37.5 flex-col gap-0.5 rounded-md border border-line bg-panel px-2.5 py-1.75'
                                : 'flex min-w-0 flex-1 basis-47.5 flex-col gap-px rounded-md border border-line bg-panel px-2.25 py-1.5'
                        }
                    >
                        <span className="text-2xs tracking-wider text-muted uppercase">
                            {translate(aspectLabels[entry.aspect])}
                        </span>

                        <span className={desktop ? 'text-base' : 'truncate text-sm'}>{entry.text}</span>

                        {desktop ? (
                            <>
                                <Owing owedBy={entry.owedBy} dueAt={entry.dueAt} />

                                {/* A conversation of one message has one message to cite, and the design draws no
                                    citation there: following it would reveal what is already on the screen. */}
                                {messages.length < 2 ? null : (
                                    <SourceLink
                                        source={sourceOf(messages, entry.sources[0]?.email)}
                                        onFollow={onFollowSource}
                                    />
                                )}
                            </>
                        ) : null}
                    </li>
                ))}
            </ul>
        </section>
    );
}

// The phone composition: one line of it, and the block itself a press away. The line says the first statement because
// that is what the design project puts there — the block is ordered by aspect, so the first line is what the
// conversation settled rather than whichever statement happened to be derived first.
function StateLine({
    entries,
    messages,
    onFollowSource,
}: {
    readonly entries: MailThreadState['entries'];
    readonly messages: readonly MailThreadMessage[];
    readonly onFollowSource: (storedEmailId: string) => void;
}) {
    const { translate } = useLocalization();
    const [shown, setShown] = useState(false);

    return (
        <>
            <div className="flex items-center gap-2.25 border-b border-line bg-sunken px-3 py-2.25">
                <span className="shrink-0 rounded-xs border border-line-strong px-1.5 py-0.5 text-2xs text-muted">
                    {translate('ai.badge')}
                </span>

                <span className="min-w-0 flex-1 truncate text-base text-text-soft">{entries[0]?.text}</span>

                {/* Icon alone, which is what the design draws at this width: the label beside it belongs to the wider
                    bands, and a bar holding a statement plus a worded control leaves the statement no room. The name
                    is the control's rather than the glyph's, so it is still what a screen reader announces. */}
                <button
                    type="button"
                    aria-label={translate('threadState.open')}
                    className="flex size-9 shrink-0 items-center justify-center rounded-full border border-line-strong text-text-soft transition hover:bg-hover"
                    onClick={() => {
                        setShown(true);
                    }}
                >
                    <Icon name="insights" className="size-4.5" />
                </button>
            </div>

            <StateSheet
                shown={shown}
                entries={entries}
                messages={messages}
                onFollowSource={onFollowSource}
                onClose={() => {
                    setShown(false);
                }}
            />
        </>
    );
}

// The whole block as a sheet rising from the foot of the window, which is where the design project puts it on a phone.
// It is a `dialog` rather than a panel of its own so that the platform traps focus inside it, answers Escape, and puts
// focus back on the control that opened it — none of which is worth a second implementation here.
function StateSheet({
    shown,
    entries,
    messages,
    onFollowSource,
    onClose,
}: {
    readonly shown: boolean;
    readonly entries: MailThreadState['entries'];
    readonly messages: readonly MailThreadMessage[];
    readonly onFollowSource: (storedEmailId: string) => void;
    readonly onClose: () => void;
}) {
    const { translate } = useLocalization();
    const sheet = useRef<HTMLDialogElement | null>(null);

    useEffect(() => {
        const panel = sheet.current;

        if (panel === null) {
            return;
        }

        if (shown && !panel.open) {
            panel.showModal();
        }

        if (!shown && panel.open) {
            panel.close();
        }
    }, [shown]);

    // It stands over the screen it was opened on, so the back gesture closes it rather than navigating out from under
    // it — the same way every other surface the client draws over one behaves.
    useScreenLayer(shown, onClose);

    return (
        <dialog
            ref={sheet}
            aria-label={translate('threadState.label')}
            className="fixed inset-x-0 top-auto bottom-0 m-0 hidden h-auto max-h-4/5 w-auto max-w-none flex-col gap-3 overflow-y-auto rounded-t-4xl border-0 bg-panel px-4 pt-2.5 pb-safe-bottom text-text shadow-dialog backdrop:bg-scrim open:flex"
            onCancel={(event) => {
                event.preventDefault();
                onClose();
            }}
            onClick={(event) => {
                if (event.target === event.currentTarget) {
                    onClose();
                }
            }}
        >
            {/* The handle the design project draws at the head of the sheet, which says a finger can push it away. */}
            <span aria-hidden="true" className="flex shrink-0 justify-center pb-1">
                <span className="h-1 w-9.5 rounded-xs bg-line-strong" />
            </span>

            <h2 className="text-xs tracking-widest text-muted uppercase">{translate('threadState.label')}</h2>

            <ul className="flex flex-col gap-2">
                {entries.map((entry, place) => (
                    <li
                        key={`${entry.aspect}-${String(place)}`}
                        className="flex flex-col gap-0.75 rounded-lg border border-line bg-sunken px-3.25 py-2.75"
                    >
                        <span className="text-2xs tracking-wider text-muted uppercase">
                            {translate(aspectLabels[entry.aspect])}
                        </span>

                        <span className="text-md">{entry.text}</span>

                        <Owing owedBy={entry.owedBy} dueAt={entry.dueAt} />

                        <SourceLink
                            source={sourceOf(messages, entry.sources[0]?.email)}
                            onFollow={(storedEmailId) => {
                                onClose();
                                onFollowSource(storedEmailId);
                            }}
                        />
                    </li>
                ))}
            </ul>

            <button
                type="button"
                className="self-end rounded-lg px-3 py-2 text-base text-text-soft transition hover:bg-hover"
                onClick={onClose}
            >
                {translate('threadState.close')}
            </button>
        </dialog>
    );
}

// Who owes a commitment and when it falls due, which is what makes it one rather than another sentence about the
// conversation. Every other aspect carries neither, and a commitment the conversation named nobody or no date for
// carries only the half it has.
function Owing({ owedBy, dueAt }: { readonly owedBy: string | null; readonly dueAt: string | null }) {
    const { locale, translate } = useLocalization();
    const due = wordInstant(dueAt, locale, 'stamp');

    if (owedBy === null && due === null) {
        return null;
    }

    return <span className="text-sm text-muted">{translate(...owing(owedBy, due))}</span>;
}

// What a commitment's owner and due date read as, as one sentence rather than two joined at the call site: which of
// the three the conversation gave is what decides the wording, and the word order between them is the language's.
function owing(owedBy: string | null, due: string | null): [MessageKey, Readonly<Record<string, string>>] {
    if (owedBy !== null && due !== null) {
        return ['threadState.owedByDue', { name: owedBy, when: due }];
    }

    return owedBy !== null ? ['threadState.owedBy', { name: owedBy }] : ['threadState.dueAt', { when: due ?? '' }];
}

// The message a statement rests on, as a control that reveals it in the conversation below. A source the conversation
// has not read yet draws nothing at all, because the link exists to take somebody to a message and one that cannot is
// a promise the screen does not keep.
function SourceLink({
    source,
    onFollow,
}: {
    readonly source: ThreadStateSource | null;
    readonly onFollow: (storedEmailId: string) => void;
}) {
    const { locale, translate } = useLocalization();

    if (source === null) {
        return null;
    }

    return (
        <button
            type="button"
            className="flex items-center gap-1 self-start rounded-xs text-sm text-accent-deep transition hover:underline"
            onClick={() => {
                onFollow(source.storedEmailId);
            }}
        >
            <Icon name="open_in_new" className="size-3.5" />

            {translate('threadState.source', {
                position: new Intl.NumberFormat(locale).format(source.position),
                name: source.name,
            })}
        </button>
    );
}
