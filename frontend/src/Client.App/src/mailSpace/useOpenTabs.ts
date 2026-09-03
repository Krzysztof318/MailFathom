// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useState } from 'react';
import { useWorkspace } from '../workspace/useWorkspace';
import {
    activated,
    closed,
    nothingOpen,
    nothingOpened,
    opened,
    tabFor,
    tabIn,
    type OpenTab,
    type OpenTabs,
} from './openTabs';

// What is open in the Mail space, and the four things a person does to it. The arithmetic is `openTabs.ts`; what this
// adds is that every one of those acts writes the workspace in the same handler — the tab set and what the reading
// column is drawing move together because one press moves both, rather than because an effect watches one and copies
// it into the other.
//
// It is why opening a message is one behaviour rather than two. The list, the search results, and anything else that
// opens one calls `openMail` whether or not the person works in tabs; what the mode decides is only whether what was
// already open stays open beside it. Nothing on the screen asks which mode it is in.

/** What is open, and how a person opens, moves between, and closes it. */
export interface OpenTabsInForce {
    readonly tabs: readonly OpenTab[];

    /** Which tab the reading column is drawing, or `null` where nothing is open. */
    readonly active: string | null;

    /**
     * Whether closing is what left nothing open, which is what decides whether the empty state takes focus.
     *
     * Closing the last tab is a view change and focus belongs at the start of what replaced it; opening the client
     * with nothing open is a landing, and moving focus there would scroll the page under somebody who has not asked
     * to go anywhere.
     */
    readonly emptiedByClosing: boolean;

    /** Opens a message — in a tab of its own where the person works in tabs, and in the pane where they do not. */
    readonly openMail: (storedEmailId: string, subject: string | null) => void;

    /**
     * Opens the surface that draws one message's own markup, the reader having been asked first.
     *
     * A tab of its own where the person works in tabs, so closing it returns to whatever else was open rather than to
     * the message it was opened from; in front of the message where they do not, so closing it returns to the message.
     */
    readonly openFullHtml: (storedEmailId: string, subject: string | null) => void;

    /** Closes that surface, whichever of the two shapes it was drawn in. */
    readonly closeFullHtml: () => void;

    /** Brings a tab forward, returning the reading column to where that tab was left. */
    readonly activate: (key: string) => void;

    /** Closes one tab, moving to the last remaining where the one closed was the one being read. */
    readonly close: (key: string) => void;

    /** Closes everything, which is the one act that needs asking first. */
    readonly closeEverything: () => void;

    /**
     * Opens what was being read when the last tab closed, or `null` where nothing has been read yet.
     *
     * It is the one way back out of an empty strip, and it returns to where that tab was left rather than to the top
     * of the message: closing everything by accident costs nothing that way.
     */
    readonly reopenLastRead: (() => void) | null;
}

/**
 * Holds what the Mail space has open.
 *
 * @param inTabs Whether the person is working in tabs, which is their preference and a window wide enough for the
 * strip. Where they are not, opening replaces what is open rather than standing beside it — so the one tab held is
 * what is on the screen, and turning the mode on finds it named rather than an empty strip over an open message.
 */
export function useOpenTabs(inTabs: boolean): OpenTabsInForce {
    const { workspace, revise } = useWorkspace();
    const [held, setHeld] = useState<OpenTabs>(nothingOpen);
    const [emptiedByClosing, setEmptiedByClosing] = useState(false);

    // The tab that was being read when it was closed, holding the place it was left at. Kept rather than derived,
    // because what it names is gone from everywhere else the moment it closes.
    const [lastRead, setLastRead] = useState<OpenTab | null>(null);

    // Where the tab being left is left: what the workspace holds at the moment somebody moves off it. Read here rather
    // than kept beside the tab, because the active tab's place is the workspace itself and a second copy of it would be
    // the pair that disagrees.
    const here = {
        selection: workspace.selection,
        conversation: workspace.conversation,
        fullHtml: workspace.fullHtml,
    };

    // Opening one thing, whatever kind it is: the tab set gains it, and the reading column goes where that tab was
    // last left. Written once because the two things a person opens differ in what they hold rather than in what
    // opening one does, and a second copy of this is how the two would come to disagree about where a tab returns to.
    function open(tab: OpenTab): void {
        setHeld((current) => opened(current, tab, here, inTabs));
        setEmptiedByClosing(false);
        revise(tabIn(held, tab.key)?.opened ?? tab.opened);
    }

    function closeTab(key: string): void {
        const after = closed(held, key);
        const going = tabIn(held, key);

        setHeld(after);
        setEmptiedByClosing(after.tabs.length === 0);

        if (held.active !== key) {
            return;
        }

        // Where it was left rather than where it was opened, so the one way back reopens the conversation the
        // reader had in front of the message as well as the message.
        setLastRead(going === null ? null : { ...going, opened: here });
        revise(after.active === null ? nothingOpened : (tabIn(after, after.active)?.opened ?? nothingOpened));
    }

    return {
        tabs: held.tabs,
        active: held.active,
        emptiedByClosing,

        openMail: (storedEmailId, subject) => {
            const tab = tabFor('thread', storedEmailId, subject, {
                selection: storedEmailId,
                conversation: null,
                fullHtml: null,
            });

            // The message already being read is already open, so opening it again is nothing — and doing the work
            // anyway would put the reading column back where this tab was opened, closing a conversation the reader
            // has in front of it.
            if (held.active === tab.key) {
                return;
            }

            open(tab);
        },

        // The surface is a tab of its own only where a person works in tabs. Where they do not there is one reading
        // column and nothing to put a second tab beside, so the surface stands in front of the message the way a
        // conversation does — which is what makes closing it a return to that message rather than to nothing.
        openFullHtml: (storedEmailId, subject) => {
            if (!inTabs) {
                revise({ fullHtml: storedEmailId });

                return;
            }

            open(
                tabFor('fullHtml', storedEmailId, subject, {
                    selection: storedEmailId,
                    conversation: null,
                    fullHtml: storedEmailId,
                }),
            );
        },

        closeFullHtml: () => {
            const active = held.active === null ? null : tabIn(held, held.active);

            if (active?.kind === 'fullHtml') {
                closeTab(active.key);

                return;
            }

            revise({ fullHtml: null });
        },

        activate: (key) => {
            const tab = tabIn(held, key);

            if (tab === null) {
                return;
            }

            // Bringing forward the tab already in front is nothing, and doing the work anyway is destructive: a tab's
            // `opened` is where it was *left*, written as it stops being active, so an active tab's copy is stale by
            // design for exactly as long as somebody is using it. Re-activating it would revise the workspace back to
            // that stale place — closing the conversation or the markup surface the reader has in front of the message,
            // which is a thing they never asked for and cannot undo. `openMail` above guards the same way for the same
            // reason; the strip calls this on every press rather than only on a press that changes something.
            if (held.active === key) {
                return;
            }

            setHeld((current) => activated(current, key, here));
            revise(tab.opened);
        },

        close: closeTab,

        closeEverything: () => {
            const going = held.active === null ? null : tabIn(held, held.active);

            setHeld(nothingOpen);
            setEmptiedByClosing(true);
            setLastRead(going === null ? null : { ...going, opened: here });
            revise(nothingOpened);
        },

        reopenLastRead:
            lastRead === null
                ? null
                : () => {
                      open(lastRead);
                  },
    };
}
