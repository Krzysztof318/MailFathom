// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { useLocalization } from '../localization/useLocalization';
import { ToastCard } from './ToastCard';
import {
    mostToastsShown,
    toastLeaving,
    ToastContext,
    type Operation,
    type OperationSettled,
    type StandingToast,
    type Toast,
    type ToastsRaised,
} from './useToasts';

// The one surface the client says things back on, mounted once above every screen. A screen asks for a toast and is
// answered with nothing: where the card is drawn, how many stand at once, and how long each lives are the
// application's decisions rather than the caller's, which is the whole reason this is a surface and not a component
// each screen owns a copy of.
//
// The corner it stands in is fixed and the region is in the document from the first paint, empty. That is what makes it
// a live region a screen reader actually announces into: a region added at the same moment as its first line is one
// several readers say nothing about at all. It is `polite` because the client interrupting somebody mid-word to say
// three threads were archived is worse than the news arriving a sentence later — an error is the exception and carries
// `role="alert"` on its own card, which is assertive where it stands.
//
// Nothing here takes focus. A toast is a statement rather than a place, so it is reachable from the keyboard by being
// in the document and never by being moved to.

/** What the provider holds: the surface every screen reaches, and the one operation only the surface itself performs. */
interface HeldToasts extends ToastsRaised {
    readonly dismiss: (id: number) => void;
}

export function ToastsProvider({ children }: { readonly children: ReactNode }) {
    const { translate } = useLocalization();
    const [standing, setStanding] = useState<readonly StandingToast[]>([]);

    // What is waiting to happen to each toast: the lifetime that dismisses it, and then the moment its leaving
    // animation is over. One timer each, because the two never overlap — dismissing cancels the lifetime by definition.
    const timers = useRef(new Map<number, ReturnType<typeof setTimeout>>());

    // What each toast has to say once its card has gone, held apart from the card because it is not drawn and because
    // it must be called exactly once: taken out as it is called, so a card closed while it is already leaving reports
    // its going once rather than twice.
    const goings = useRef(new Map<number, () => void>());
    const raised = useRef(0);

    // Which cards were standing when that was last read. It is what makes a going one thing rather than three: a card
    // leaves because its lifetime ran out, because somebody closed it, or because the bound pushed it off the end of
    // the list, and only the last of those has no code path of its own to report from. A card pushed out with its
    // going unreported is the defect this exists against — the offer a card carries is open for exactly as long as the
    // card is, so a permanent delete buried under two later toasts would otherwise be held by the deployment for the
    // whole of a window nobody is watching any more, and its rows would go on saying they were being deleted.
    const stood = useRef<readonly number[]>([]);

    // The one place a going is reported, which is what the rule above amounts to in code: it is read off the cards that
    // have left rather than called by whatever took them away. An effect because what it reaches is outside React
    // entirely — a callback the caller registered and this surface promised to call once.
    useEffect(() => {
        const standingNow = new Set(standing.map((toast) => toast.id));

        for (const id of stood.current) {
            if (standingNow.has(id)) {
                continue;
            }

            // The lifetime of a card pushed off the end is cancelled with it: what it was waiting to do is dismiss a
            // card that is no longer there. A card that is merely leaving is still in the list and keeps its own.
            clearTimeout(timers.current.get(id));
            timers.current.delete(id);

            const going = goings.current.get(id);

            goings.current.delete(id);
            going?.();
        }

        stood.current = [...standingNow];
    }, [standing]);

    useEffect(
        () => () => {
            for (const timer of timers.current.values()) {
                clearTimeout(timer);
            }

            timers.current.clear();

            // Deliberately not called. The surface going with the tab is the one case where nothing is left to report
            // a going to, and calling them here would fire every standing offer at the moment the page is unloading.
            goings.current.clear();
        },
        [],
    );

    // The surface is built once rather than per render. Every screen holding it would otherwise re-render each time a
    // toast arrives or goes, which is the whole application re-rendering for a card in the corner; each function below
    // reaches only `setStanding` and the two refs, all three of which are stable for the life of the provider.
    const toasts = useMemo<HeldToasts>(() => {
        function wait(id: number, until: number, then: () => void): void {
            clearTimeout(timers.current.get(id));
            timers.current.set(
                id,
                setTimeout(() => {
                    timers.current.delete(id);
                    then();
                }, until),
            );
        }

        function dismiss(id: number): void {
            setStanding((current) => current.map((toast) => (toast.id === id ? { ...toast, leaving: true } : toast)));

            // The card is taken out once its leaving animation is over, and nothing is reported from here: what the
            // card was offering is closed by its having left, which the effect above reads off the list.
            wait(id, toastLeaving, () => {
                setStanding((current) => current.filter((toast) => toast.id !== id));
            });
        }

        function show(toast: StandingToast): void {
            // Newest first, and past the bound the oldest goes rather than the newest being refused: what somebody was
            // just told is what they are most likely still reading.
            setStanding((current) => [toast, ...current].slice(0, mostToastsShown));
        }

        function raise(said: Toast, standFor: number): void {
            raised.current += 1;

            const id = raised.current;

            if (said.whenGone !== undefined) {
                goings.current.set(id, said.whenGone);
            }

            show({
                id,
                title: said.title,
                body: said.body,
                action: said.action,
                leaving: false,
                stands: { kind: said.kind },
                standFor,
            });

            wait(id, standFor, () => {
                dismiss(id);
            });
        }

        function raiseOperation(operation: Operation, standFor: number): OperationSettled {
            raised.current += 1;

            const id = raised.current;

            // No lifetime is armed: an operation still running is the one toast that does not take itself away, because
            // its disappearing would say it had finished.
            show({
                id,
                title: operation.title,
                body: operation.body,
                action: undefined,
                leaving: false,
                stands: { operation },
                standFor,
            });

            return (outcome) => {
                if (outcome.whenGone !== undefined) {
                    goings.current.set(id, outcome.whenGone);
                }

                setStanding((current) =>
                    current.map((toast) =>
                        toast.id === id
                            ? {
                                  ...toast,
                                  title: outcome.title,
                                  body: outcome.body,
                                  action: outcome.action,
                                  stands: { kind: outcome.kind },
                              }
                            : toast,
                    ),
                );

                // It becomes the outcome where it already stands rather than being replaced by a second card, and its
                // own lifetime starts from there — so what somebody was watching is what tells them how it went.
                wait(id, standFor, () => {
                    dismiss(id);
                });
            };
        }

        return { raise, raiseOperation, dismiss };
    }, []);

    // Stopping is the surface's own act rather than the card's: the card asks the question, and what an answer of yes
    // means — the operation told to stop, its toast taken away, and a warning saying nothing was written — is one
    // sequence stated here so no caller has to remember two thirds of it.
    function stopOperation(toast: StandingToast): void {
        if (!('operation' in toast.stands)) {
            return;
        }

        toast.stands.operation.stop();
        toasts.dismiss(toast.id);
        toasts.raise(
            {
                kind: 'warning',
                title: translate('toast.stopped'),
                body: translate('toast.stoppedNothingWritten'),
            },
            toast.standFor,
        );
    }

    return (
        <ToastContext value={toasts}>
            {children}

            <ol
                aria-label={translate('toast.surface')}
                aria-live="polite"
                className="pointer-events-none fixed inset-x-3 top-3 z-60 mt-safe-top flex flex-col gap-2.5 workspace:inset-x-auto workspace:top-4.5 workspace:right-4.5 workspace:w-87.5"
            >
                {standing.map((toast) => (
                    <li key={toast.id}>
                        <ToastCard
                            toast={toast}
                            onDismiss={() => {
                                toasts.dismiss(toast.id);
                            }}
                            onStop={() => {
                                stopOperation(toast);
                            }}
                        />
                    </li>
                ))}
            </ol>
        </ToastContext>
    );
}
