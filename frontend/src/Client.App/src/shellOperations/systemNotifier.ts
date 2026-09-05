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
// **A browser is one of the heads that can answer it**, which is the part #1609 left for #1664. The Notifications API
// is the operating system's own surface reached from a page rather than a second mechanism beside the shell's, so a
// web head resolves to a second implementation of this same operation and nothing above learns that it did. What
// separates the two is asked in order: a shell that linked the notification plugin answers first, because that plugin
// installs its own replacement for `window.Notification` before the bundle runs, and a browser question put first
// would reach the replacement while believing it had reached the browser.
//
// **What it may carry is a count and a kind, and that bound is the reason this module exists at all.** A notification
// lands in the operating system's own action centre and on its lock screen, which is storage MailFathom cannot retain,
// redact, or erase — so a sender, a subject, a fragment of a body, an attachment name, and an address each stay behind
// the click. `notifications/arrivalCounts.ts` is what reduces an arrival to the two numbers this is allowed to say, and
// it is asserted there rather than here, because a bound proved at the edge is a bound one caller can still walk past.
// A browser notification lands in exactly the same places, so it is the same bound rather than a second one.
//
// **The permission is asked once and its refusal is final.** The plugin's own desktop path grants unconditionally, so
// on Windows and Linux this resolves to a grant the first time it is asked; a head that refuses — Android, once #1616
// links the plugin and grants it the capability — answers `refused` for the rest of the run and is never asked again.
// What the caller does with that refusal is turn the behaviour off on this device, which is
// `preferences/systemNotifications.ts`.
//
// **Who is asked, and when, is where the two heads genuinely differ.** The plugin grants without a dialog, so a shell
// head may be asked by the first arrival and nobody notices. A browser puts a dialog in front of a person, several
// browsers refuse the call unless a gesture they made led to it, and a browser standing at `denied` cannot be asked
// again from script at all. So the ask is an operation of its own — `permit` — called where a gesture exists, which is
// the settings switch, and `raise` asks nobody anything. What a head already stands able to do before that gesture is
// `standing`, which is what lets the switch draw the truth rather than a default the web head could not keep.
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
// `src-tauri/src/notifications.rs` keeps that handle instead, which is what makes `whenActedOn` below possible —
// somebody acting on a notification is an event the shell says out loud, and a head that raises none says nothing.
// A browser needs no such split: `Notification.onclick` is the platform's own, so the web head satisfies that same
// member out of the notifications it raised rather than out of a bridge.

/**
 * What raising one ended as, which is three answers rather than two because two of them are not the same fact.
 *
 * `refused` is the operating system's own answer and belongs on the device permanently. `unavailable` is this head
 * having no such operation, which nobody decided and which must therefore change nothing a person would have to undo.
 */
export type NotificationRaised = 'raised' | 'refused' | 'unavailable';

/**
 * What this head stands able to do before anybody has been asked anything, which is what a switch draws rather than
 * guesses.
 *
 * `permitted` is a head that would reach the operating system if it raised one now — a shell whose plugin grants on
 * demand, or a browser somebody has already allowed. `refused` was said by somebody, and asking again reaches nobody.
 * `unasked` is a question nobody has put yet, and it is the one of the three a gesture can still change.
 */
export type NotificationStanding = 'permitted' | 'refused' | 'unasked';

/** Raising a system notification, and whether the head this bundle is running in can raise one at all. */
export interface SystemNotifier {
    /**
     * Whether this head carries the operation, which is the whole of what decides the client's behaviour here.
     *
     * Where it is `false` there is no operation, no setting to draw, and nothing to ask permission for: a head whose
     * shell linked no notification plugin behaves exactly as it did before this existed, and so does a page served
     * outside a secure context, where the browser withholds the API rather than refusing a call into it.
     */
    readonly offered: boolean;

    /** What this head already stands able to do, which the settings switch draws and never has to ask to find out. */
    readonly standing: NotificationStanding;

    /**
     * Asks whoever decides, and answers what they said.
     *
     * It is called from a gesture and from nowhere else, because a browser refuses the prompt outright unless one was
     * made — and because a dialog an arrival raised is a dialog raised over whatever somebody was reading. Asking
     * twice is asking once, except where the question reached nobody: a prompt somebody closed without answering is
     * put again by the next gesture.
     */
    readonly permit: () => Promise<NotificationStanding>;

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

/** What the browser's three permission words mean here, which are the same three facts under other names. */
function standingFromBrowser(permission: NotificationPermission): NotificationStanding {
    if (permission === 'granted') {
        return 'permitted';
    }

    return permission === 'denied' ? 'refused' : 'unasked';
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
    if (shellOffersNotifying()) {
        return raisedThroughTheShell();
    }

    return browserOffersNotifying() ? raisedThroughTheBrowser() : raisesNothing;
}

/** What a head that offered no such command answers, and what every caller reads before it asks for anything. */
const raisesNothing: SystemNotifier = {
    offered: false,
    standing: 'unasked',
    permit: () => Promise.resolve('unasked'),
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

// The same question of the browser, and it is two questions for a reason of its own. A browser withholds the API
// entirely outside a secure context, and this repository permits a client endpoint served without TLS where a
// deployment declared it — so a page reached over plain `http` at anything but `localhost` carries no such operation,
// which is the answer a head that linked no plugin gives and is drawn the same way. `isSecureContext` is the browser's
// own reading of that condition, the localhost exemption included, so nothing here parses an address.
function browserOffersNotifying(): boolean {
    return globalThis.isSecureContext && typeof globalThis.Notification === 'function';
}

/**
 * The operation the shell answers, holding the permission answer for the run.
 *
 * The answer is held as the promise rather than as its value, so two arrivals landing in the same tick — or an arrival
 * and somebody moving the settings switch — ask the operating system once between them instead of racing to open two
 * of its dialogs.
 *
 * It stands `permitted` before anybody is asked, which is what the plugin's desktop path makes true: it grants
 * unconditionally, so a switch drawn off until somebody had answered a dialog that never appears would describe a
 * machine that does not exist. A head that does refuse — Android, once #1616 links the plugin — says so at the first
 * arrival, and `useNotificationCentre.ts` is what writes that refusal onto the device.
 */
function raisedThroughTheShell(): SystemNotifier {
    let permitted: Promise<NotificationRaised> | null = null;

    async function permission(): Promise<NotificationRaised> {
        permitted ??= askOnce();

        return permitted;
    }

    return {
        offered: true,
        standing: 'permitted',
        permit: async () => standingFromRaising(await permission()),
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
 * The operation the browser answers, which puts a question to nobody until a gesture does.
 *
 * **`Notification.permission` is read at every call and held nowhere**, because it is the browser's own record and
 * somebody may change it from the address bar while the client is open. A copy taken once would be a client insisting
 * it had been refused by a person who has since allowed it — and the settings dialog is remounted every time it is
 * opened, so a copy taken at composition would also be redrawing app-start truth over an answer given since. What is
 * held is only the prompt itself, and only for as long as one is actually open.
 *
 * The act somebody takes on one is held here rather than reached for at each raise, because that is the shape the
 * shell established and the caller subscribes once: every notification this raises is wired to whatever is listening
 * when it is raised, so a head with nothing subscribed raises one that does nothing when clicked.
 */
function raisedThroughTheBrowser(): SystemNotifier {
    let asking: Promise<NotificationStanding> | null = null;
    let acted: (() => void) | null = null;

    return {
        offered: true,

        get standing() {
            return standingFromBrowser(Notification.permission);
        },

        // The prompt is held only while it is open, so two gestures in the same moment raise one dialog between them
        // and nothing is remembered once it closes. Every answer is then re-derived from the browser's own record on
        // the next gesture — including a grant, because a person may take one back from the address bar, and a held
        // *yes* is how this client would come to write **on** onto a device the browser has since refused. Asking
        // again costs no second dialog: a browser standing at anything but `default` answers without opening one.
        permit: async () => {
            asking ??= askTheBrowser();

            try {
                return await asking;
            } finally {
                asking = null;
            }
        },

        raise: (said) => {
            // Never a prompt. An arrival is not a gesture, so a browser nobody has put the question to yet answers
            // `unavailable` rather than a refusal — which is kept nowhere, for the reason an absent operation is:
            // nobody decided it.
            const standing = standingFromBrowser(Notification.permission);

            if (standing !== 'permitted') {
                return Promise.resolve(standing === 'refused' ? 'refused' : 'unavailable');
            }

            try {
                const raised = new Notification(said);

                // Bringing the window to the front is the browser's half of answering a click, and is this head's
                // alone: the shell's own event arrives at a window the operating system has already raised.
                raised.onclick = () => {
                    window.focus();
                    raised.close();
                    acted?.();
                };

                return Promise.resolve('raised');
            } catch {
                // A browser that exposes the constructor and refuses to construct one, which is what Chrome on Android
                // does — it wants a service worker to show it instead. Nobody decided that either.
                return Promise.resolve('unavailable');
            }
        },

        whenActedOn: (act) => {
            acted = act;

            return () => {
                if (acted === act) {
                    acted = null;
                }
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

/**
 * Puts the browser's question to whoever is in front of it, once.
 *
 * A browser standing at `denied` is not asked: the specification has the call resolve `denied` without a prompt, and
 * reading the record first is what makes that a stated rule of this module rather than a behaviour inherited from one.
 *
 * **A prompt somebody closed without answering resolves `default`, and that is not a refusal.** Chrome's dialog has a
 * dismissal beside its two answers, and reading it as *no* would write the permanent off onto the device on behalf of
 * a person who said nothing — which is the same mistake as reading an absent operation as one. So the browser's three
 * answers are carried across as three, and only `denied` is somebody's decision.
 */
async function askTheBrowser(): Promise<NotificationStanding> {
    const standing = standingFromBrowser(Notification.permission);

    if (standing !== 'unasked') {
        return standing;
    }

    try {
        return standingFromBrowser(await Notification.requestPermission());
    } catch {
        return 'unasked';
    }
}

/** What a raise's three answers say about where a head stands, which is the same fact asked a different way. */
function standingFromRaising(raised: NotificationRaised): NotificationStanding {
    if (raised === 'raised') {
        return 'permitted';
    }

    return raised === 'refused' ? 'refused' : 'unasked';
}
