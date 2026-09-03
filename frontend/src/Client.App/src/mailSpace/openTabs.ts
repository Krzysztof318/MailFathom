// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { OpenedAttachment } from '../workspace/openAttachment';
import type { OpenConversation } from '../workspace/openConversation';

// What the Mail space has open when a person works in tabs, as values rather than as anything on the screen. The strip
// draws it, the reading column draws one of them, and everything below is the arithmetic between: what opening
// something twice does, what closing the active one leaves behind, and what closing everything leaves.
//
// **The active tab holds nothing of its own.** What it shows is the workspace's — the message open and the conversation
// standing in front of it — so a tab's `opened` is the place it was left at, written as it stops being active and read
// as it becomes active again. That is the whole reason there is no second copy of what is on the screen to keep in
// step: the pane reads the workspace exactly as it did before tabs existed, and every screen that opens or closes a
// conversation goes on doing it without knowing a tab is holding its place.

/**
 * What the reading column draws: the message open, and whichever surface stands in front of it.
 *
 * All of them are the workspace's own, read from it as a tab loses focus and written back to it as one gains focus, so
 * this is one shape with the workspace rather than a second copy of what is on the screen.
 */
export interface OpenedMail {
    readonly selection: string | null;
    readonly conversation: OpenConversation | null;

    /** The message whose own markup is being shown, or `null` where the reduced tree is what is being read. */
    readonly fullHtml: string | null;

    /** The file being read in front of the message, or `null` where the message itself is what is being read. */
    readonly attachment: OpenedAttachment | null;
}

/**
 * The four things the design project gives a tab of its own.
 *
 * One of them has no screen behind it yet — a draft is the composer of #1210 — so nothing constructs one today. It is
 * named here rather than added later because the strip is what draws whichever kind a tab has, and a kind it could not
 * draw would be a strip rewritten by that change instead of a tab handed to it.
 */
export type OpenTabKind = 'thread' | 'attachment' | 'fullHtml' | 'draft';

/** One thing open, as the strip names it and as the reading column returns to it. */
export interface OpenTab {
    /** What this tab is, which is its kind and what it holds — so opening the same thing twice finds it. */
    readonly key: string;

    readonly kind: OpenTabKind;

    /** What the strip calls it — a subject, a file name — or `null` where the thing carries no name of its own. */
    readonly title: string | null;

    /** Where this tab was left, read when it becomes active again and meaningless while it is. */
    readonly opened: OpenedMail;
}

/** Everything open, and which one the reading column is drawing. */
export interface OpenTabs {
    readonly tabs: readonly OpenTab[];
    readonly active: string | null;
}

/** Nothing open at all, which is where a person starts and what closing everything leaves. */
export const nothingOpen: OpenTabs = { tabs: [], active: null };

/** Nothing being read, which is what a tab that is not a message was opened beside. */
export const nothingOpened: OpenedMail = { selection: null, conversation: null, fullHtml: null, attachment: null };

/**
 * One tab, identified by what it holds.
 *
 * @param kind What sort of thing it is.
 * @param id What the thing is, as the service names it — a message, an attachment, a draft.
 * @param title What the strip calls it, or `null` where it has no name of its own.
 * @param opened What the reading column draws for it, which today a message and a file it carries have.
 */
export function tabFor(
    kind: OpenTabKind,
    id: string,
    title: string | null,
    opened: OpenedMail = nothingOpened,
): OpenTab {
    return { key: `${kind}:${id}`, kind, title, opened };
}

/** The tab under a key, or `null` where nothing open carries it. */
export function tabIn(state: OpenTabs, key: string): OpenTab | null {
    return state.tabs.find((tab) => tab.key === key) ?? null;
}

/**
 * Everything open after something is opened.
 *
 * A tab already open is activated rather than added a second time, which is what keeps the strip a map of what is open
 * instead of a history of what was clicked. What was active is left holding where it was, so returning to it returns to
 * the place rather than to the top.
 *
 * @param state Everything open.
 * @param tab The tab being opened.
 * @param leaving Where the tab losing focus is being left, which is what the workspace holds at that moment.
 * @param beside Whether what is already open stays open, which is what working in tabs means. Where it does not, this
 * is the one tab there is — so turning the mode back on finds the thing on the screen named rather than an empty strip
 * over an open message.
 */
export function opened(state: OpenTabs, tab: OpenTab, leaving: OpenedMail, beside: boolean): OpenTabs {
    if (!beside) {
        return { tabs: [tab], active: tab.key };
    }

    const held = left(state, leaving);

    return {
        tabs: tabIn(state, tab.key) === null ? [...held.tabs, tab] : held.tabs,
        active: tab.key,
    };
}

/**
 * Everything open after another tab is brought forward.
 *
 * @param state Everything open.
 * @param key The tab being brought forward.
 * @param leaving Where the tab losing focus is being left.
 * @returns Everything open, unchanged where no tab carries the key.
 */
export function activated(state: OpenTabs, key: string, leaving: OpenedMail): OpenTabs {
    return tabIn(state, key) === null ? state : { tabs: left(state, leaving).tabs, active: key };
}

/**
 * Everything open after one tab is closed.
 *
 * Closing the active one moves to the last remaining, which is where the design project leaves a reader; closing any
 * other leaves what is on the screen alone.
 */
export function closed(state: OpenTabs, key: string): OpenTabs {
    const tabs = state.tabs.filter((tab) => tab.key !== key);

    if (state.active !== key) {
        return { tabs, active: state.active };
    }

    return { tabs, active: tabs[tabs.length - 1]?.key ?? null };
}

// The tab losing focus, holding the place it is being left at. Everything else is untouched, and a state whose active
// tab has already gone is left alone rather than given somebody else's place.
function left(state: OpenTabs, leaving: OpenedMail): OpenTabs {
    return {
        tabs: state.tabs.map((tab) => (tab.key === state.active ? { ...tab, opened: leaving } : tab)),
        active: state.active,
    };
}
