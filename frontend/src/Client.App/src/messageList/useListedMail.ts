// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { createContext, useContext } from 'react';
import type { ActedMessage } from '../mailboxActs/useMailboxActs';

// The message list as the surfaces outside it reach it, which is two questions and nothing else: where a message the
// list has drawn belongs, and *select everything*.
//
// It exists because the workspace keeps a selection as identities alone — which is what lets a selection outlive the
// pages the list has scrolled away from — while an act on one has to name the account it is in and the folder it is
// leaving. The list is the only thing that ever knew either, so this is where it says so.
//
// **Selecting everything is the list's own act**, not a rewrite of the selection from outside: what *everything* means
// is the rows the list is holding, which is a window over a folder rather than the folder. So the bar draws the
// control and the list performs it, and a screen with no list on it performs nothing.
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

    /** Says which list is on the screen, and `null` as it leaves. */
    readonly listing: (selectingAll: (() => void) | null) => void;
}

/** What a tree with no provider above it reads, which is a client where nothing outside a list can reach into one. */
export const nothingListed: ListedMail = {
    placeOf: () => null,
    drew: () => undefined,
    selectAll: () => undefined,
    listing: () => undefined,
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
