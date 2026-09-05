// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { createContext, useContext } from 'react';
import { isPermissionGranted, requestPermission } from '@tauri-apps/plugin-notification';

// Saying something while nobody is looking at the window is the second operation only a shell can perform, and it takes
// the shape `linkOpener.ts` beside it established: the application declares what it needs, one function resolves which
// implementation answers, `main.tsx` calls that function once, and everything below receives the operation through
// context. No component, hook, or screen asks which head it is running on — they ask whether the operation is offered,
// which is a question about this run rather than about an operating system.
//
// **What it may carry is a count and a kind, and that bound is the reason this module exists at all.** A notification
// lands in the operating system's own action centre and on its lock screen, which is storage MailFathom cannot retain,
// redact, or erase — so a sender, a subject, a fragment of a body, an attachment name, and an address each stay behind
// the click. `notifications/arrivalCounts.ts` is what reduces an arrival to the two numbers this is allowed to say, and
// it is asserted there rather than here, because a bound proved at the edge is a bound one caller can still walk past.
//
// **The permission is asked once and its refusal is final.** The plugin's own desktop path grants unconditionally, so
// on Windows and Linux this resolves to a grant the first time it is asked; a head that refuses — Android, once #1616
// links the plugin and grants it the capability — answers `refused` for the rest of the run and is never asked again.
// What the caller does with that refusal is turn the behaviour off on this device, which is
// `preferences/systemNotifications.ts`.
//
// **A refusal and an absent operation are not the same answer**, which is what makes the answer three values rather
// than two. A refusal was given by somebody and is kept; an operation this head never carried was decided by nobody
// and is kept nowhere. Conflating them is how a head with no notification plugin — the Android one until #1616 — would
// write *off* on a machine whose owner was never asked anything.
//
// **Raising one and being permitted to are two different bridges, and the split is not arbitrary.** Permission is the
// plugin's, because it is the half whose answer differs between heads and the half the phone will need. Raising is
// this shell's own command, because the plugin's desktop path reports no click: it hands the notification to
// `notify_rust` and drops the handle, so a person who clicks the thing the client showed them lands nowhere.
// `src-tauri/src/notifications.rs` keeps that handle instead, which is what makes the third member below possible —
// somebody acting on a notification is an event the shell says out loud, and a head that raises none says nothing.

/**
 * What raising one ended as, which is three answers rather than two because two of them are not the same fact.
 *
 * `refused` is the operating system's own answer and belongs on the device permanently. `unavailable` is this head
 * having no such operation, which nobody decided and which must therefore change nothing a person would have to undo.
 */
export type NotificationRaised = 'raised' | 'refused' | 'unavailable';

/** Raising a system notification, and whether the head this bundle is running in can raise one at all. */
export interface SystemNotifier {
    /**
     * Whether this head carries the operation, which is the whole of what decides the client's behaviour here.
     *
     * Where it is `false` there is no operation, no setting to draw, and nothing to ask permission for: the web head
     * behaves exactly as it did before this existed, and so does a head whose shell linked no notification plugin.
     */
    readonly offered: boolean;

    /**
     * Raises one notification saying the sentence given, answering what became of it.
     *
     * One sentence and no second line, because what a notification may carry is a count and a kind — the operating
     * system draws the product's own name above it, so there is nothing left for a body to say that would not be mail.
     */
    readonly raise: (said: string) => Promise<NotificationRaised>;

    /**
     * Subscribes to somebody acting on a notification this raised, answering with what stops listening.
     *
     * It carries nothing, because a notification carries a count and a kind: which arrival was clicked is a question
     * about mail, and where that is answered is the centre this opens. A head that raises no notification subscribes
     * to nothing and answers with a stop that stops nothing, so a caller subscribes unconditionally rather than asking
     * which head it is on first.
     */
    readonly whenActedOn: (act: () => void) => () => void;
}

/** What the shell calls the event, which is the one name this module and `src-tauri/src/notifications.rs` share. */
const actedOn = 'system-notification-acted-on';

export const SystemNotifierContext = createContext<SystemNotifier | null>(null);

export function useSystemNotifier(): SystemNotifier {
    const notifier = useContext(SystemNotifierContext);

    if (notifier === null) {
        throw new Error('A component asked to notify outside the SystemNotifierContext that main.tsx supplies.');
    }

    return notifier;
}

/** Resolves the operation for the head this bundle is running in, which is the whole of the composition. */
export function systemNotifierForThisApplication(): SystemNotifier {
    return shellOffersNotifying() ? raisedThroughTheShell() : raisesNothing;
}

/** What a head that offered no such command answers, and what every caller reads before it asks for anything. */
const raisesNothing: SystemNotifier = {
    offered: false,
    raise: () => Promise.resolve('unavailable'),
    whenActedOn: () => () => undefined,
};

// Two questions rather than one, and the second is what a shell operation for a *plugin* costs that `linkOpener.ts`
// did not pay. A shell announces itself by putting its own bridge on the global object before the bundle runs, which is
// what the first line asks — but the opener plugin is linked into every head and this one is not, so a bridge being
// present says nothing about this operation. What says it is the binding: the plugin's own script replaces
// `window.Notification` before the bundle runs, and the permission question this module puts goes through that
// replacement, so a head that linked no notification plugin has nothing there to call. Asking after the binding rather
// than after the operating system is what keeps this a question about the operation instead of one about which head is
// underneath. It answers for the shell's own `raise_notification` beside it as well, and does so honestly rather than
// by luck: `src-tauri/Cargo.toml` states the plugin per target and `src-tauri/src/notifications.rs` does nothing on
// exactly the targets it is left off, so the two halves of this operation are linked and unlinked together.
function shellOffersNotifying(): boolean {
    return Object.hasOwn(globalThis, '__TAURI_INTERNALS__') && typeof globalThis.Notification === 'function';
}

/**
 * The operation the shell answers, holding the permission answer for the run.
 *
 * The answer is held as the promise rather than as its value, so two arrivals landing in the same tick ask the
 * operating system once between them instead of racing to open two of its dialogs.
 */
function raisedThroughTheShell(): SystemNotifier {
    let permitted: Promise<NotificationRaised> | null = null;

    async function permission(): Promise<NotificationRaised> {
        permitted ??= askOnce();

        return permitted;
    }

    return {
        offered: true,
        raise: async (said) => {
            const answer = await permission();

            if (answer !== 'raised') {
                return answer;
            }

            // Raising itself answers nothing and cannot: the command hands the notification to the operating system
            // and returns, so what is reported here is that permission stood rather than that a window appeared. The
            // answer that matters is the one above, and it is the same gate — the permission question is a command of
            // the plugin's, so a shell that granted this webview neither has already said so there.
            void window.__TAURI__?.core.invoke('raise_notification', { said });

            return 'raised';
        },
        whenActedOn: (act) => {
            // Subscribing is itself asynchronous and unsubscribing is what the shell answers with, so what is held is
            // the promise rather than its value — a caller that stops listening before the subscription landed must
            // still stop the one that is about to.
            const listening = window.__TAURI__?.event.listen(actedOn, act);

            return () => {
                void listening?.then((stop) => {
                    stop();
                });
            };
        },
    };
}

/**
 * Asks the operating system once, and separates the two ways of not being allowed.
 *
 * A grant or a refusal is an answer somebody gave, so `denied` is theirs to keep. A call that throws is not an answer
 * at all — the binding is missing, or the shell granted this webview none of the plugin's commands — and turning that
 * into a refusal would write *off* on the device without the operating system or the person ever having been asked.
 */
async function askOnce(): Promise<NotificationRaised> {
    try {
        if (await isPermissionGranted()) {
            return 'raised';
        }

        return (await requestPermission()) === 'granted' ? 'raised' : 'refused';
    } catch {
        return 'unavailable';
    }
}
