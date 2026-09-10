// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { createContext, useContext, useMemo } from 'react';

// What the client says back about what somebody just did, and the whole of the contract a screen reaches it through.
// The context and its hook sit apart from the provider that fills them for the reason
// `localization/useLocalization.ts` gives: a module Vite hot-reloads may export components alone.
//
// A toast is not a notification and never becomes one. It has no read state, no history, and no store — it is said,
// it stands for a few seconds, and it is gone — which is why nothing here persists anything and why the surface is
// raised from the application rather than owned by whichever screen happened to cause the outcome.

/**
 * How long a toast stands before it takes itself away, where the person has said nothing about it.
 *
 * It is the design project's value and the deployment's own unset answer, which is why the same number stands in on
 * both sides: a client with no session and one whose person has never chosen behave alike.
 */
export const toastLifetime = 5_000;

/** How long it takes to leave once it has been dismissed, which is what its own animation is given. */
export const toastLeaving = 200;

/** How many toasts stand at once. Past this the oldest goes, so a burst never buries the screen behind it. */
export const mostToastsShown = 4;

/**
 * What a toast says it is.
 *
 * `neutral` confirms an ordinary operation and takes no colour, `success` and `error` report what an operation came
 * to, `warning` is a consequence somebody has to know about without anything having failed, and `info` is the client
 * saying something of its own rather than answering the last click.
 */
export type ToastKind = 'neutral' | 'success' | 'error' | 'warning' | 'info';

/** The one thing a toast may offer beyond being read: undo, retry, show. Never two. */
export interface ToastAction {
    readonly label: string;
    readonly take: () => void;
}

/** Something that has happened, in the words the person reading it gets. */
export interface Toast {
    readonly kind: ToastKind;
    readonly title: string;
    readonly body?: string;
    readonly action?: ToastAction;

    /**
     * Called once when the card goes, whether it took itself away or somebody closed it.
     *
     * What it is for is the offer the card was making: a toast whose action is the way back out of something is also
     * the whole of how long that way back is open, so its going is when the client stops holding one. It is called
     * whether or not the action was taken — the caller knows which of the two happened and this does not — and it is
     * not called when the surface itself goes with the tab, which is the one case nothing can be reported to anybody.
     */
    readonly whenGone?: () => void;
}

/** Something that is happening, which stands until it is finished or stopped rather than for a lifetime. */
export interface Operation {
    readonly title: string;
    readonly body?: string;

    /**
     * What stopping this operation would leave behind, in the words the person gets before they answer.
     *
     * Required rather than defaulted: what is lost by stopping halfway is the operation's own to say, and a generic
     * sentence in its place is the confirmation that teaches somebody to click through the next one. It carries the
     * name `blocking/useBlocking.ts` gives the same sentence, because an operation that blocks the client and one
     * that reports from the corner are two surfaces for one act rather than two ideas.
     */
    readonly stoppingLeavesBehind: string;

    /** Stops the operation. Called once, and only after somebody confirmed they meant to. */
    readonly stop: () => void;
}

/** Reports what an operation came to, which turns the toast that was following it into that outcome in place. */
export type OperationSettled = (outcome: Toast) => void;

/**
 * One toast as the surface holds it: what it says, whether it is on its way out, and what it stands for.
 *
 * The last of those is a union rather than a kind beside an operation, because the two would otherwise be a pair that
 * has to agree — an operation still running is exactly a toast with no settled kind yet, and no state can be in both
 * halves at once.
 */
export interface StandingToast {
    readonly id: number;
    readonly title: string;

    // Stated rather than optional, unlike the two the caller fills: what is held is built here and always carries both
    // names, so a toast that has just settled cannot keep the body of the operation it used to be.
    readonly body: string | undefined;
    readonly action: ToastAction | undefined;
    readonly leaving: boolean;
    readonly stands: { readonly kind: ToastKind } | { readonly operation: Operation };

    /**
     * How long this one was raised to stand for, kept so the surface's own answers stand for as long.
     *
     * The surface is mounted above the frame that reads the person's preference, so it cannot ask what a toast of its
     * own should stand for; a toast it raises in answer to one that is standing takes that one's.
     */
    readonly standFor: number;
}

export interface ToastSurface {
    /** Says what just happened. */
    readonly raise: (toast: Toast) => void;

    /**
     * Says an operation has started, and answers with what settles it.
     *
     * The toast it raises does not go on its own: it stands until the operation settles, or until somebody closes it
     * and confirms that closing it means stopping the operation.
     */
    readonly raiseOperation: (operation: Operation) => OperationSettled;
}

/**
 * The surface as the provider actually offers it, which takes how long each toast is to stand.
 *
 * How long that is belongs to the person rather than to the caller, and the person's answer arrives inside the frame —
 * below the surface, which is mounted above everything that outlives a screen. So the value travels the only way it
 * can: it is read from the context below at the point a screen asks for the surface, and stamped onto what it raises.
 * A caller states what happened and nothing about how long it is read for, which is why the public contract carries
 * neither parameter.
 */
export interface ToastsRaised {
    readonly raise: (toast: Toast, standFor: number) => void;
    readonly raiseOperation: (operation: Operation, standFor: number) => OperationSettled;
}

export const ToastContext = createContext<ToastsRaised | null>(null);

/**
 * How long a toast stands, in milliseconds, which the frame states from the person's own preference.
 *
 * Its default is what a client with no session draws with — the sign-in screen, and every moment before the deployment
 * has answered — and is the same number the deployment answers a person who has chosen nothing.
 */
export const ToastLifetimeContext = createContext(toastLifetime);

export function useToasts(): ToastSurface {
    const raised = useContext(ToastContext);
    const standFor = useContext(ToastLifetimeContext);

    const surface = useMemo<ToastSurface | null>(
        () =>
            raised === null
                ? null
                : {
                      raise: (toast) => {
                          raised.raise(toast, standFor);
                      },
                      raiseOperation: (operation) => raised.raiseOperation(operation, standFor),
                  },
        [raised, standFor],
    );

    if (surface === null) {
        throw new Error('A component raised a toast outside the ToastsProvider that main.tsx mounts.');
    }

    return surface;
}
