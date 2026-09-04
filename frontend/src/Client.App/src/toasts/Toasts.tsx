// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { useLocalization } from '../localization/useLocalization';
import { ToastCard } from './ToastCard';
import {
    mostToastsShown,
    toastLeaving,
    toastLifetime,
    ToastContext,
    type Operation,
    type OperationSettled,
    type StandingToast,
    type Toast,
    type ToastSurface,
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

/** What the provider holds: the surface every screen gets, and the one operation only the surface itself performs. */
interface HeldToasts extends ToastSurface {
    readonly dismiss: (id: number) => void;
}

export function ToastsProvider({ children }: { readonly children: ReactNode }) {
    const { translate } = useLocalization();
    const [standing, setStanding] = useState<readonly StandingToast[]>([]);

    // What is waiting to happen to each toast: the lifetime that dismisses it, and then the moment its leaving
    // animation is over. One timer each, because the two never overlap — dismissing cancels the lifetime by definition.
    const timers = useRef(new Map<number, ReturnType<typeof setTimeout>>());
    const raised = useRef(0);

    useEffect(
        () => () => {
            for (const timer of timers.current.values()) {
                clearTimeout(timer);
            }

            timers.current.clear();
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

            wait(id, toastLeaving, () => {
                setStanding((current) => current.filter((toast) => toast.id !== id));
            });
        }

        function show(toast: StandingToast): void {
            // Newest first, and past the bound the oldest goes rather than the newest being refused: what somebody was
            // just told is what they are most likely still reading.
            setStanding((current) => [toast, ...current].slice(0, mostToastsShown));
        }

        function raise(said: Toast): void {
            raised.current += 1;

            const id = raised.current;

            show({
                id,
                title: said.title,
                body: said.body,
                action: said.action,
                leaving: false,
                stands: { kind: said.kind },
            });

            wait(id, toastLifetime, () => {
                dismiss(id);
            });
        }

        function raiseOperation(operation: Operation): OperationSettled {
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
            });

            return (outcome) => {
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
                wait(id, toastLifetime, () => {
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
        toasts.raise({
            kind: 'warning',
            title: translate('toast.stopped'),
            body: translate('toast.stoppedNothingWritten'),
        });
    }

    return (
        <ToastContext value={toasts}>
            {children}

            <ol
                aria-label={translate('toast.surface')}
                aria-live="polite"
                className="pointer-events-none fixed inset-x-3 top-3 z-60 mt-safe-top flex flex-col gap-2.5 workspace:inset-x-auto workspace:top-4.5 workspace:right-4.5 workspace:w-100"
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
