// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { createContext, useContext } from 'react';

// What this client has marked read since it was opened, and the one way of adding to it. It belongs to the whole
// application rather than to a screen, because three unrelated places read it: the row that draws a message, the folder
// tree that counts unread mail, and the body that marks one on being drawn.
//
// It exists at all because the deployment goes on reporting a message unread until the account's own pass has told the
// mail server and observed the answer. That is what a durable mutation is, and it is minutes rather than milliseconds —
// so a client drawing only what the deployment last said would leave a message somebody has just read looking unread
// until then. What is held here is the pending mutation and nothing else: MailFathom stores no reading of its own, and
// this goes when the tab does.
//
// The context and its hook sit apart from the provider that fills them for the reason `workspace/useWorkspace.ts`
// gives: a module Vite hot-reloads may export components alone.

/** The message a marking is about: what names it, where it was counted as unread, and whether it still is. */
export interface MessageOpened {
    readonly storedEmailId: string;
    readonly account: string;
    readonly folder: string;

    /** Whether the deployment last reported the message without `\Seen`, which is the only case there is anything to mark. */
    readonly unread: boolean;
}

/** Where a message this client marked read was counted as unread, which is what a folder's count is corrected by. */
export interface MarkedIn {
    readonly account: string;
    readonly folder: string;
}

export interface ReadMarking {
    /**
     * The messages this client has marked read, by the message it was marked on.
     *
     * The map rather than two questions answered about it, because both of its readers ask something different: a row
     * asks whether one message is in it, and the folder tree counts the ones in a folder. Deriving either from the
     * other would be a second answer to keep in step with the first.
     */
    readonly marked: ReadonlyMap<string, MarkedIn>;

    /**
     * Marks a message read because its body has been drawn in front of the person reading it.
     *
     * Safe to call again for a message already marked, and for one the deployment already reports read: neither
     * submits anything. A client whose reader turned marking off, or whose credential may not write a flag, is handed
     * an implementation that marks nothing, so nothing below has to ask.
     */
    readonly markRead: (message: MessageOpened) => void;
}

/**
 * What a tree with no provider above it reads, which is a client that marks nothing.
 *
 * A default rather than the refusal `useWorkspace` raises, because marking nothing is a state this application really
 * has — the reader turned it off, the grant is absent, or there is no session — and a screen drawn under any of those
 * is drawn from the remote flag alone. Nothing below the provider distinguishes them, which is the point: the three
 * are one behaviour and this is it.
 */
export const nothingMarkedRead: ReadMarking = {
    marked: new Map(),
    markRead: () => undefined,
};

export const ReadMarkingContext = createContext<ReadMarking>(nothingMarkedRead);

export function useReadMarking(): ReadMarking {
    return useContext(ReadMarkingContext);
}

/** Whether a row is drawn unread: what the deployment last reported, less what this client has marked since. */
export function drawnUnread(marking: ReadMarking, storedEmailId: string, unread: boolean): boolean {
    return unread && !marking.marked.has(storedEmailId);
}
