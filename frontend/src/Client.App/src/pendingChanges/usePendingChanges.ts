// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { createContext, useContext } from 'react';
import type { ChangeSubmission, ChangeStanding, PendingChange } from './changeStandings';

// The changes this client has asked its mailbox for and is still following, and the two ways of adding to that and of
// taking something out of it. It belongs to the whole application rather than to a screen for the reason
// `readMarking/useReadMarking.ts` gives about its own: what asks for a change and what says where changes stand are
// different parts of the frame, and neither is above the other.
//
// The context and its hook sit apart from the provider that fills them for the reason `workspace/useWorkspace.ts`
// gives: a module Vite hot-reloads may export components alone.

/** How often the client asks where its own changes have got to, while any of them is still on its way. */
export const followedChangeInterval = 5_000;

/**
 * The most consecutive times the client asks and is not answered before it stops asking and hands the retry over.
 *
 * Bounded rather than endless because what is being followed has already been written down durably: the change is not
 * at risk while nobody is watching it, so asking forever would spend a phone's battery to learn nothing.
 */
export const mostFollowingAttempts = 5;

/** A change the deployment could not settle, and which of the two things it could not settle about it. */
export interface UndecidedChange extends PendingChange {
    readonly standing: Extract<ChangeStanding, 'exhausted' | 'unanswered'>;
}

/** What a person may do about a change the deployment could not settle. Both are acts; neither is a default. */
export type ChangeResolution = 'askAgain' | 'letGo';

export interface PendingChanges {
    /** The changes on their way to a mailbox, oldest first, which is the order they were asked for in. */
    readonly waiting: readonly PendingChange[];

    /** The changes waiting on a person rather than on a mailbox. */
    readonly undecided: readonly UndecidedChange[];

    /** Whether the client has stopped asking where its changes stand, having been unanswered too many times running. */
    readonly stoppedFollowing: boolean;

    /** Follows what one submission became: what was written down is waited on, and what was refused is reported. */
    readonly follow: (submission: ChangeSubmission) => void;

    /** Settles one change the deployment could not, by asking for it again or by letting it go. */
    readonly settle: (recordId: string, resolution: ChangeResolution) => void;

    /** Asks again where the followed changes stand, from a person asking rather than from the client trying on its own. */
    readonly followAgain: () => void;
}

/**
 * What a tree with no provider above it reads, which is a client that follows nothing.
 *
 * A default rather than a refusal, for the reason `readMarking/useReadMarking.ts` gives about the same shape: a client
 * with no session, or one whose credential may change no flag, really does follow nothing, and every screen below is
 * drawn the same way under either.
 */
export const nothingPending: PendingChanges = {
    waiting: [],
    undecided: [],
    stoppedFollowing: false,
    follow: () => undefined,
    settle: () => undefined,
    followAgain: () => undefined,
};

export const PendingChangesContext = createContext<PendingChanges>(nothingPending);

export function usePendingChanges(): PendingChanges {
    return useContext(PendingChangesContext);
}
