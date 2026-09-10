// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { createContext, useContext } from 'react';
import type { ActedMessage } from '../mailboxActs/useMailboxActs';
import type { StandingView } from './listing';

// The message list as the surfaces outside it reach it, which is two questions and nothing else: where a message the
// list has drawn belongs, and *select everything*. Two more are here for the reason the second one is, and the two
// paragraphs below say what that reason is: taking focus back, and standing on one of the tree's views.
//
// It exists because the workspace keeps a selection as identities alone — which is what lets a selection outlive the
// pages the list has scrolled away from — while an act on one has to name the account it is in and the folder it is
// leaving. The list is the only thing that ever knew either, so this is where it says so.
//
// **Focus is the third question**, and it is here for the same reason: the selection bar disappears the moment it
// clears the selection, taking the pressed control with it, and the place a reader was before they picked messages out
// is the row the list left focus on. Only the list knows which row that is.
//
// **Selecting everything is the list's own act**, not a rewrite of the selection from outside: what *everything* means
// is the rows the list is holding, which is a window over a folder rather than the folder. So the bar draws the
// control and the list performs it, and a screen with no list on it performs nothing.
//
// **Standing on one of the folder tree's views is the same shape.** A view is a set of filter criteria on the folder
// in front of the reader, and a folder's filters belong to the list rather than to the tree — so the tree asks and the
// list narrows itself, exactly as its own filter panel does, and a tree drawn beside no list narrows nothing.
//
// The context and its hook sit apart from the provider that fills them for the reason `workspace/useWorkspace.ts`
// gives: a module Vite hot-reloads may export components alone.

// The most messages whose place is remembered, oldest dropped first. It is a bound rather than a policy: a selection
// is built by hand out of what somebody has scrolled past, and the folder behind it holds hundreds of thousands of
// messages — so a map that grew with the reading would grow without end. Far above any selection a person builds and
// far below anything worth measuring.
export const mostPlacesRemembered = 10_000;

export interface ListedMail {
    /** Where a message the list has drawn belongs, or `null` for one it never drew. */
    readonly placeOf: (storedEmailId: string) => ActedMessage | null;

    /** Writes down where the mail of a page that has just arrived belongs. */
    readonly drew: (
        emails: readonly { readonly id: string; readonly account: string; readonly folder: string }[],
    ) => void;

    /** Selects every message the list is showing, and does nothing where no list is on the screen. */
    readonly selectAll: () => void;

    /** Puts focus back on the row the list left it on, which is where a bar above it hands focus before it goes. */
    readonly takeFocus: () => void;

    /** Says which list is on the screen, and `null` as it leaves. */
    readonly listing: (list: ListedMailbox | null) => void;

    /** Puts one of the tree's standing views in force on the list, and does nothing where no list is on the screen. */
    readonly stand: (view: StandingView) => void;

    /** Reads the leading end of the list again, and does nothing where no list is on the screen. */
    readonly readAgain: () => void;
}

/** What a list on the screen answers for, filled by the list itself and by nothing above it. */
export interface ListedMailbox {
    readonly selectAll: () => void;
    readonly takeFocus: () => void;

    /**
     * Puts one of the tree's standing views in force, which is the list's own act for the reason selecting everything
     * is: what a view sets is filters on the folder in front of the reader, and the folder's filters are the list's.
     */
    readonly stand: (view: StandingView) => void;

    /**
     * Reads the leading end of the list again, which is the list's own act for the reason the two above are: what the
     * leading end *is* — which page, read with which cursor, under which filters — is the list's alone, and a refresh
     * asked from the rail knows none of it.
     */
    readonly readAgain: () => void;
}

/** What a tree with no provider above it reads, which is a client where nothing outside a list can reach into one. */
export const nothingListed: ListedMail = {
    placeOf: () => null,
    drew: () => undefined,
    selectAll: () => undefined,
    takeFocus: () => undefined,
    listing: () => undefined,
    stand: () => undefined,
    readAgain: () => undefined,
};

export const ListedMailContext = createContext<ListedMail>(nothingListed);

export function useListedMail(): ListedMail {
    return useContext(ListedMailContext);
}

/** The messages an act is about, which are the ones picked out that a list has drawn at some point in this session. */
export function actedMessages(listed: ListedMail, storedEmailIds: readonly string[]): readonly ActedMessage[] {
    const messages: ActedMessage[] = [];

    for (const storedEmailId of storedEmailIds) {
        const message = listed.placeOf(storedEmailId);

        if (message !== null) {
            messages.push(message);
        }
    }

    return messages;
}
