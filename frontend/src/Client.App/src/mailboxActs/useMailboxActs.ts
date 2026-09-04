// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { createContext, useContext } from 'react';
import type { MailTimelineEntry } from '@mailfathom/client-backend';
import type { ActRefusal, MoveDestination } from './mailboxDestinations';

// The five things a person does to their own mailbox from the Mail space, as an operation on messages rather than as
// anything on a screen. It belongs to the application rather than to the toolbar, because four surfaces reach the same
// five acts — the toolbar over what is open, the bar over a selection, the row that draws one as pending, and the
// message somebody swiped — and a second implementation of *archive* is how two of them come to file mail differently.
//
// Nothing here reaches a mail server. Each act writes a durable record through `/api/client` and answers; the account's
// own convergence pass is what tells the server, which is why an unreachable account leaves an act pending rather than
// failing it, and why what this holds is what was asked for rather than what has been observed.
//
// The context and its hook sit apart from the provider that fills them for the reason `workspace/useWorkspace.ts`
// gives: a module Vite hot-reloads may export components alone.

/**
 * The five acts, named for what a person asked for rather than for the route each travels.
 *
 * Three of them are folder moves and two are flags, which is a fact about the mail server rather than about the
 * screen: a control says *archive*, and where that lands is `mailboxDestinations.ts`'s to answer.
 */
export type MailboxAct = 'flag' | 'markUnread' | 'archive' | 'delete' | 'move';

/** One message an act is about: what names it, and where it is, which is what filing and taking that back both need. */
export interface ActedMessage {
    readonly storedEmailId: string;
    readonly account: string;

    /** The folder the message is in, which is where taking a move back puts it. */
    readonly folder: string;
}

export interface MailboxActs {
    /**
     * What this client has asked for and the deployment has not been seen to have applied, by the message it is about.
     *
     * Held rather than derived, for the reason `readMarking/useReadMarking.ts` gives about its own: a mutation is
     * durable the moment it is written down and converges minutes later, so a screen drawing only what the deployment
     * last reported would show mail somebody has just filed as though nothing had happened. It goes when the tab does.
     */
    readonly asked: ReadonlyMap<string, MailboxAct>;

    /** Why the act cannot be performed on those messages, or `null` where it can. */
    readonly refusalOf: (act: MailboxAct, messages: readonly ActedMessage[]) => ActRefusal | null;

    /** The folders those messages could be filed into, which are their one account's. */
    readonly destinationsOf: (messages: readonly ActedMessage[]) => readonly MoveDestination[];

    /**
     * Performs an act and reports what it came to through the toast surface.
     *
     * Safe to call for an act `refusalOf` refuses: nothing is submitted, so a control that was drawn before the answer
     * arrived cannot file mail into a folder that is not there.
     */
    readonly perform: (act: MailboxAct, messages: readonly ActedMessage[], destination?: MoveDestination) => void;
}

/**
 * What a tree with no provider above it reads, which is a client that changes nothing.
 *
 * A default rather than the refusal `useWorkspace` raises, because changing nothing is a state this application really
 * has — no session, a credential without the grant, or a deployment that serves no mail — and every one of them draws
 * the same client. Nothing below distinguishes them, which is the point.
 */
export const nothingActed: MailboxActs = {
    asked: new Map(),
    refusalOf: () => 'notOffered',
    destinationsOf: () => [],
    perform: () => undefined,
};

export const MailboxActsContext = createContext<MailboxActs>(nothingActed);

export function useMailboxActs(): MailboxActs {
    return useContext(MailboxActsContext);
}

/**
 * The act a row is still waiting on, or `null` where it is waiting on none.
 *
 * Nothing polls for convergence: the row itself is what says the change arrived. A flag this client asked for is
 * pending until the deployment reports the message flagged, and a message asked to be marked unread is pending until
 * it is reported unread — so the sentence goes on its own the moment the account's pass has been round. The three that
 * file a message elsewhere have no such flag to watch, and their rows leave the folder on the next read of it.
 */
export function actPending(acts: MailboxActs, email: MailTimelineEntry): MailboxAct | null {
    const act = acts.asked.get(email.id) ?? null;

    if (act === 'flag') {
        return email.flagged ? null : act;
    }

    if (act === 'markUnread') {
        return email.unread ? null : act;
    }

    return act;
}
