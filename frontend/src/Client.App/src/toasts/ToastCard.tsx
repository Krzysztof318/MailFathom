// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useEffect, useId, useRef, useState, type PointerEvent } from 'react';
import { Icon } from '../controls/Icon';
import type { IconName } from '../controls/icons';
import { swipeSoFar } from '../controls/swipeDismissal';
import type { MessageKey } from '../localization/en';
import { useLocalization } from '../localization/useLocalization';
import { toastLeaving, toastLifetime, type StandingToast, type ToastAction, type ToastKind } from './useToasts';

// One toast as it stands on the screen: what happened, what it was about, at most one thing to do about it, and the
// close control that is on every card in every composition. The bar along the bottom edge is the lifetime running out,
// which is why the toast that is still following an operation has none — nothing is running out for it.
//
// Two ways to get rid of one, and both are here because both have to be. The close control is the design project's and
// it never gives way to the gesture: a card whose only dismissal is a swipe is a card a mouse and a keyboard cannot
// reach. The swipe is beside it where a finger is what is being used, and it is the same threshold and the same
// vertical cancellation the rest of the client's swipes answer to, stated once in `controls/swipeDismissal.ts`.
//
// Neither way is quieter than the other. Where the card is following an operation, both ask the same question and stop
// it only on the same answer — a gesture that aborted something a button would have asked about would be the fastest
// way in the client to lose work by accident.

/** What each kind is drawn as: its symbol, the tint behind it, the bar it runs out on, and what it is called. */
interface ToastMark {
    readonly icon: IconName;
    readonly tint: string;
    readonly bar: string;
    readonly said: MessageKey;
}

// A lookup declared once rather than a chain inside the markup, exhaustive by its own type, so the six kinds the
// design project draws are one table a reader sees the whole of. Colour reaches the symbol and the bar and nothing
// else: a card that took the colour of what it says would be six cards rather than one surface.
const toastMarks: Readonly<Record<ToastKind | 'running', ToastMark>> = {
    neutral: { icon: 'info', tint: 'bg-hover text-text-soft', bar: 'bg-line-strong', said: 'toast.neutral' },
    success: {
        icon: 'check_circle',
        tint: 'bg-healthy-soft text-healthy-text',
        bar: 'bg-healthy',
        said: 'toast.success',
    },
    error: { icon: 'error', tint: 'bg-error-soft text-error-text', bar: 'bg-error', said: 'toast.error' },
    warning: {
        icon: 'warning',
        tint: 'bg-warning-soft text-warning-text',
        bar: 'bg-warning',
        said: 'toast.warning',
    },
    info: { icon: 'campaign', tint: 'bg-accent-soft text-accent-strong', bar: 'bg-accent', said: 'toast.info' },
    running: {
        icon: 'progress_activity',
        tint: 'bg-accent-soft text-accent-strong',
        bar: 'bg-accent',
        said: 'toast.running',
    },
};

const wordsStop = 'stop';

export function ToastCard({
    toast,
    onDismiss,
    onStop,
}: {
    readonly toast: StandingToast;

    /** Takes the card away, which is what closing one that is not following an operation means. */
    readonly onDismiss: () => void;

    /** Stops the operation the card is following, once somebody has confirmed that is what they meant. */
    readonly onStop: () => void;
}) {
    const { translate } = useLocalization();
    const asked = useRef<HTMLDialogElement>(null);
    const question = useId();

    // The gesture in flight: which pointer started it and where it landed. A ref rather than state, because nothing on
    // the screen is drawn from it and a moving finger would otherwise render the card for every pixel of its travel.
    const swiping = useRef<{ pointer: number; from: number; top: number } | null>(null);

    // The sentence the question is asking, held here rather than read from the toast while the dialog is open. The
    // operation goes on running while somebody decides, so it may settle mid-question and take `stands` with it — and
    // a dialog whose words came from what has just changed would be unmounted under the reader with focus inside it.
    const [asking, setAsking] = useState<string | null>(null);

    // Bound once rather than read inside the handler: the handler runs after this render, and a property read there
    // is no longer the one the markup decided to draw a control for.
    const action = toast.action;
    const running = 'operation' in toast.stands;
    const mark = running ? toastMarks.running : toastMarks[toast.stands.kind];
    const closing = running ? translate('toast.stopOperation') : translate('toast.close');

    // Two imperative calls on a dialog, which is the whole of what these effects are for. Opening it is `showModal`
    // rather than an attribute, for the top layer, the inertness, the focus trap, and the backdrop; closing it is the
    // dialog's own path, which returns focus to whatever opened it. The second is what an operation settling while the
    // question stands has to go through — the answer is no longer worth anything, and unmounting the element instead
    // would drop the focus that `showModal` trapped inside it.
    useEffect(() => {
        if (asking !== null) {
            asked.current?.showModal();
        }
    }, [asking]);

    useEffect(() => {
        if (!running) {
            asked.current?.close();
        }
    }, [running]);

    function close(): void {
        if ('operation' in toast.stands) {
            setAsking(toast.stands.operation.stoppingLeavesBehind);
        } else {
            onDismiss();
        }
    }

    function take(action: ToastAction): void {
        onDismiss();
        action.take();
    }

    function beginSwipe(event: PointerEvent<HTMLDivElement>): void {
        // A mouse drags nothing away: the close control is what a pointer that can hover already has, and a card that
        // vanished under a slipped mouse button would be a statement lost to a twitch.
        if (event.pointerType === 'mouse' || swiping.current !== null) {
            return;
        }

        swiping.current = { pointer: event.pointerId, from: event.clientX, top: event.clientY };

        try {
            event.currentTarget.setPointerCapture(event.pointerId);
        } catch {
            // A runtime not tracking this pointer refuses to hand its capture over, which is what a synthesised event
            // is. The gesture then follows the handlers on the card instead, which is everything but the part where it
            // keeps following a finger that has left it.
        }
    }

    function followSwipe(event: PointerEvent<HTMLDivElement>): void {
        const swipe = swiping.current;

        if (swipe?.pointer !== event.pointerId) {
            return;
        }

        const verdict = swipeSoFar(event.clientX - swipe.from, event.clientY - swipe.top);

        if (verdict === 'travelling') {
            return;
        }

        swiping.current = null;

        if (verdict === 'dismissing') {
            close();
        }
    }

    function endSwipe(event: PointerEvent<HTMLDivElement>): void {
        if (swiping.current?.pointer === event.pointerId) {
            swiping.current = null;
        }
    }

    return (
        <div
            // An error is the one thing said here that cannot wait for a gap in what a screen reader is already
            // reading, so it announces where it stands rather than politely with the rest of the surface.
            role={!running && toast.stands.kind === 'error' ? 'alert' : undefined}
            className={`pointer-events-auto relative flex touch-none items-start gap-3 overflow-hidden rounded-3xl border border-line bg-panel ps-3.25 pe-3 pt-3.25 pb-3.75 opacity-80 shadow-overlay transition hover:opacity-100 ${
                toast.leaving ? 'animate-toast-leaving' : 'animate-toast-arriving'
            }`}
            // How long leaving takes is how long the surface waits before the card is gone, which is one number in
            // `useToasts.ts` and is handed to the animation here rather than written into the stylesheet a second time.
            style={toast.leaving ? { animationDuration: `${String(toastLeaving)}ms` } : undefined}
            onPointerDown={beginSwipe}
            onPointerMove={followSwipe}
            onPointerUp={endSwipe}
            onPointerCancel={endSwipe}
        >
            <span className={`flex size-8.5 shrink-0 items-center justify-center rounded-xl ${mark.tint}`}>
                <Icon name={mark.icon} className={`size-5.25 ${running ? 'animate-spin' : ''}`} />
            </span>

            <div className="flex min-w-0 flex-1 flex-col gap-1 pt-0.5">
                <p className="text-md font-semibold text-text text-pretty">
                    {/* What the symbol and its colour say to everybody else. Said before the title rather than after
                        it, so somebody hearing the card knows what kind of news it is before they hear the news. */}
                    <span className="sr-only">{translate(mark.said)}</span> {toast.title}
                </p>

                {toast.body === undefined ? null : <p className="text-base text-text-soft text-pretty">{toast.body}</p>}

                {action === undefined ? null : (
                    <button
                        type="button"
                        className="mt-1.25 self-start rounded-lg border border-line-strong px-3.25 py-1.5 text-base font-semibold text-text transition hover:bg-hover"
                        onClick={() => {
                            // The card goes as the action is taken, which is the design project's own behaviour and
                            // the honest one: a toast still offering to undo something already undone is a control
                            // somebody presses twice.
                            take(action);
                        }}
                    >
                        {action.label}
                    </button>
                )}
            </div>

            <button
                type="button"
                aria-label={closing}
                title={closing}
                className="flex size-7.5 shrink-0 items-center justify-center rounded-lg text-muted transition hover:bg-hover hover:text-text"
                onClick={close}
            >
                <Icon name="close" className="size-4.75" />
            </button>

            {running ? null : (
                // The lifetime, drawn. Its duration is the one the toast is actually held for rather than a second
                // copy of that number written into the stylesheet, which is why it arrives here as a value.
                <span
                    aria-hidden="true"
                    className={`absolute inset-x-0 bottom-0 h-0.5 origin-left animate-toast-lifetime opacity-55 ${mark.bar}`}
                    style={{ animationDuration: `${String(toastLifetime)}ms` }}
                />
            )}

            {asking === null ? null : (
                <dialog
                    ref={asked}
                    aria-labelledby={question}
                    className="m-auto w-96 max-w-full rounded-3xl border border-line bg-panel p-5 text-text shadow-dialog backdrop:bg-scrim"
                    onClose={() => {
                        // Every way out of the dialog arrives here — both controls, the escape key, and the operation
                        // settling underneath it — and only one of them carries the word that stops anything.
                        if (asked.current?.returnValue === wordsStop) {
                            onStop();
                        }

                        setAsking(null);
                    }}
                >
                    <div className="flex flex-col gap-3.5">
                        <h2 id={question} className="text-xl font-semibold">
                            {translate('toast.stopQuestion')}
                        </h2>

                        <p className="text-base text-muted text-pretty">{asking}</p>

                        <div className="flex flex-wrap justify-end gap-2">
                            <button
                                type="button"
                                className="rounded-lg px-3.75 py-2 text-base text-text-soft transition hover:bg-hover"
                                onClick={() => {
                                    asked.current?.close();
                                }}
                            >
                                {translate('toast.keepGoing')}
                            </button>

                            <button
                                type="button"
                                className="rounded-lg bg-error px-4 py-2 text-base font-semibold text-on-accent transition hover:opacity-90"
                                onClick={() => {
                                    asked.current?.close(wordsStop);
                                }}
                            >
                                {translate('toast.stopOperation')}
                            </button>
                        </div>
                    </div>
                </dialog>
            )}
        </div>
    );
}
