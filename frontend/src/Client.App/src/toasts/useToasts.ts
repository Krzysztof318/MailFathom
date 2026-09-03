// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { createContext, useContext } from 'react';

// What the client says back about what somebody just did, and the whole of the contract a screen reaches it through.
// The context and its hook sit apart from the provider that fills them for the reason
// `localization/useLocalization.ts` gives: a module Vite hot-reloads may export components alone.
//
// A toast is not a notification and never becomes one. It has no read state, no history, and no store — it is said,
// it stands for a few seconds, and it is gone — which is why nothing here persists anything and why the surface is
// raised from the application rather than owned by whichever screen happened to cause the outcome.

/** How long a toast stands before it takes itself away, which is the design project's value. */
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
}

/** Something that is happening, which stands until it is finished or stopped rather than for a lifetime. */
export interface Operation {
    readonly title: string;
    readonly body?: string;

    /**
     * What stopping this operation would cost, in the words the person gets before they answer.
     *
     * Required rather than defaulted: what is lost by stopping halfway is the operation's own to say, and a generic
     * sentence in its place is the confirmation that teaches somebody to click through the next one.
     */
    readonly stopExplanation: string;

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

export const ToastContext = createContext<ToastSurface | null>(null);

export function useToasts(): ToastSurface {
    const surface = useContext(ToastContext);

    if (surface === null) {
        throw new Error('A component raised a toast outside the ToastsProvider that main.tsx mounts.');
    }

    return surface;
}
