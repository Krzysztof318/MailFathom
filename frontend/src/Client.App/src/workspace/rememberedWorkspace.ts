// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { longestSearchText } from '@mailfathom/client-backend';
import { mostAskedQuestions, withoutFragmentText, type AskedQuestion, type AskScope } from './askScope';
import { everything, isMailFolderRole, type MailScope } from './mailScope';
import type { OpenConversation } from './openConversation';
import { emptyWorkspace, type Workspace } from './useWorkspace';

// Where the workspace survives a reload, which a single-page application makes a cold start rather than a way out:
// reloading already returns to the same deployment and the same signed-in person, so returning them to a folder tree
// they had folded shut and a mailbox they had chosen is the same promise kept one level further in.
//
// The session's store rather than the machine's, deliberately. What a person is looking at and what they were about to
// ask are theirs rather than the machine's — the frame already empties the workspace when the credential goes, and a
// store that dies with the tab is what makes that true of a tab somebody closed without signing out. It is the same
// bound the web head keeps its credential under, for the same reason.
//
// Reached as `window.sessionStorage` rather than as the bare global for the reason `device/deviceStore.ts` gives:
// Node publishes stores of its own that win over the document's under the test runner.
const storageKey = 'mailfathom.workspace';

// What a stored workspace may carry before it is read as somebody's edit rather than as this client's own writing. A
// tree holds tens of rows, a question is a sentence, and an identifier — a message, an account, a folder alias — is a
// name the service assigned, so each of the three is far above anything the client itself writes there.
const mostToggledFolds = 512;
const longestQuestion = 4_096;
const longestIdentifier = 256;

// What a selection may hold before it is read as somebody's edit. The list keeps a bounded number of pages, so a reader
// who selects every row it is holding selects a few hundred; this is above that and bounds what one tab writes here.
const mostSelectedMessages = 1_024;

/**
 * How many searches are offered back before the oldest is dropped.
 *
 * Read here and written by the screen that keeps them, so the bound a store is read under and the bound one tab writes
 * under are one number rather than two that drift.
 */
export const mostRecentSearches = 8;

// A folded row is keyed by the scope it stands for, so at its longest it names an account and a folder's whole place on
// its mail server rather than one identifier.
const longestRow = 1_024;

/** What this tab was last looking at, or an empty workspace where nothing was kept or what was kept is not one. */
export function rememberedWorkspace(): Workspace {
    let stored: string | null;

    try {
        stored = window.sessionStorage.getItem(storageKey);
    } catch {
        return emptyWorkspace;
    }

    if (stored === null) {
        return emptyWorkspace;
    }

    let parsed: unknown;

    try {
        parsed = JSON.parse(stored);
    } catch {
        return emptyWorkspace;
    }

    return workspaceIn(parsed) ?? emptyWorkspace;
}

/**
 * Keeps what this tab is looking at, so a reload returns to it.
 *
 * Everything but three values, each left out for a reason of its own. The selected fragment is a passage of somebody's
 * mail rather than a name the service assigned, so keeping it would put mail content in a browser store for nothing —
 * the reading pane drops the fragment as the message it belongs to opens, and a reload is that message opening again.
 * The full-HTML surface is left out because it is a consent rather than a place: it was given for one message after
 * being told what a stranger's markup can carry, and a surface that reopened itself on a reload would be that consent
 * remembered. So a reload returns to the message with the surface closed, and pressing the control asks again. A file
 * is left out because it is the sender's own name for something a reader asked to see once, and returning to it would
 * fetch the file again on a reload nobody meant as a request for it — so a reload returns to the message, which is
 * where the file was opened from and is one press away. A file a search result cited goes with it, being the same act
 * half finished.
 *
 * A question asked about a passage is kept and the passage is not, for the same reason the selected fragment is left
 * out: the question is a sentence somebody typed, and the words they highlighted are a piece of somebody's mail. What
 * is stored beside the question is the message the passage came from, which is a name the service assigned.
 */
export function rememberWorkspace(workspace: Workspace): void {
    try {
        window.sessionStorage.setItem(
            storageKey,
            JSON.stringify({
                ...workspace,
                fragment: null,
                fullHtml: null,
                attachment: null,
                citedAttachment: null,
                askedBefore: workspace.askedBefore.map(withoutFragmentText),
            }),
        );
    } catch {
        // A browser refusing storage still runs the client; what a person was looking at then lasts the run rather
        // than outliving it, which is a smaller loss than a client that fails over a preference.
    }
}

// Read back as untrusted input, because a store is a place a person can write. Anything this client did not write is
// answered as nothing kept rather than as a workspace with a hole in it, which is what would otherwise reach a screen
// as a scope naming a mailbox that does not exist.
function workspaceIn(value: unknown): Workspace | null {
    if (typeof value !== 'object' || value === null || Array.isArray(value)) {
        return null;
    }

    const record = value as Record<string, unknown>;
    const scope = scopeIn(record['scope']);
    const chosen = record['askScopeKey'] ?? null;
    const askScopeKey = chosen === null ? null : namedScopeKeyIn(chosen);
    const askedBefore = askedBeforeIn(record['askedBefore'] ?? []);
    const foldsToggled = foldsToggledIn(record['foldsToggled'] ?? []);
    const mailboxesFolded = record['mailboxesFolded'] ?? false;
    const panelsHidden = record['panelsHidden'] ?? false;
    const selection = record['selection'] ?? null;
    const selected = selectedIn(record['selected']);
    const question = record['question'];
    const recentSearches = recentSearchesIn(record['recentSearches'] ?? []);
    const conversation = conversationIn(record['conversation'] ?? null);

    if (
        scope === null ||
        (chosen !== null && askScopeKey === null) ||
        askedBefore === null ||
        foldsToggled === null ||
        selected === null ||
        recentSearches === null ||
        conversation === undefined
    ) {
        return null;
    }

    if (selection !== null && !isIdentifier(selection)) {
        return null;
    }

    if (typeof mailboxesFolded !== 'boolean' || typeof panelsHidden !== 'boolean') {
        return null;
    }

    if (typeof question !== 'string' || question.length > longestQuestion) {
        return null;
    }

    // Neither the fragment nor the full-HTML surface is read back, because neither was written: what a store holds for
    // them is whatever somebody put there by hand, and reading that would be this client opening a surface nobody
    // pressed a control to open.
    return {
        scope,
        foldsToggled,
        mailboxesFolded,
        panelsHidden,
        selection,
        conversation,
        fullHtml: null,
        attachment: null,
        citedAttachment: null,
        fragment: null,
        selected,
        question,
        askScopeKey,
        askedBefore,
        recentSearches,
    };
}

// The questions asked before, each held to the same bound the question being typed is held to: what a store carries
// for them is read back into state and written out again on every revision, and neither the sentence nor the list is
// something this client would have written past those numbers.
function askedBeforeIn(value: unknown): readonly AskedQuestion[] | null {
    if (!Array.isArray(value) || value.length > mostAskedQuestions) {
        return null;
    }

    const asked: AskedQuestion[] = [];
    for (const entry of value) {
        if (typeof entry !== 'object' || entry === null || Array.isArray(entry)) {
            return null;
        }

        const question = (entry as Record<string, unknown>)['question'];
        const scope = askScopeIn((entry as Record<string, unknown>)['scope']);

        if (
            typeof question !== 'string' ||
            question.length === 0 ||
            question.length > longestQuestion ||
            scope === null
        ) {
            return null;
        }

        asked.push({ question, scope });
    }

    return asked;
}

// What somebody pointed the field at, which is a key rather than a scope and is checked as one: it names a scope only
// against the list the field is offering, and `askScope.ts` falls back to the screen for a key that names nothing there
// now. So nothing here has to know which scopes a control can produce, and a key that has gone stale costs a fallback
// rather than a question asked about a folder nobody has. The bound is a folded row's, that being the same shape — an
// account and a folder's whole place on its mail server.
function namedScopeKeyIn(value: unknown): string | null {
    return typeof value === 'string' && value.length > 0 && value.length <= longestRow ? value : null;
}

// Read through the same checks the workspace's own values are read through, because that is exactly what these shapes
// hold: a mail scope, a correspondence the deployment named, a list of messages it named, and one message.
//
// A passage is refused rather than read, because none was written: `rememberWorkspace` reduces a question asked about
// one to the message it came from, so a fragment scope in a store is somebody's own writing and reading it back would
// be this client putting a stranger's words into a question's scope.
function askScopeIn(value: unknown): AskScope | null {
    if (typeof value !== 'object' || value === null || Array.isArray(value)) {
        return null;
    }

    const record = value as Record<string, unknown>;
    const threadId = record['threadId'];
    const messageId = record['messageId'];

    switch (record['kind']) {
        case 'mail': {
            const scope = scopeIn(record['scope']);

            return scope === null ? null : { kind: 'mail', scope };
        }
        case 'thread':
            return isIdentifier(threadId) ? { kind: 'thread', threadId } : null;
        case 'message':
            return isIdentifier(messageId) ? { kind: 'message', messageId } : null;
        case 'selection': {
            const messages = selectedIn(record['messages']);

            return messages === null || messages.length === 0 ? null : { kind: 'selection', messages };
        }
        default:
            return null;
    }
}

// The searches read back, each held to what this surface ranks against at all: text longer than that is not a search
// this client wrote, and a list longer than the bound is not one it kept.
function recentSearchesIn(value: unknown): readonly string[] | null {
    if (!Array.isArray(value) || value.length > mostRecentSearches) {
        return null;
    }

    const searches: string[] = [];
    for (const searched of value) {
        if (typeof searched !== 'string' || searched.length === 0 || searched.length > longestSearchText) {
            return null;
        }

        searches.push(searched);
    }

    return searches;
}

// Answers `undefined` for a shape it refuses, because `null` is what a tab reading a single message legitimately kept.
function conversationIn(value: unknown): OpenConversation | null | undefined {
    if (value === null) {
        return null;
    }

    if (typeof value !== 'object' || Array.isArray(value)) {
        return undefined;
    }

    const record = value as Record<string, unknown>;
    const threadId = record['threadId'];
    const openAt = record['openAt'] ?? null;

    if (!isIdentifier(threadId) || (openAt !== null && !isIdentifier(openAt))) {
        return undefined;
    }

    return { threadId, openAt };
}

function selectedIn(value: unknown): readonly string[] | null {
    if (!Array.isArray(value) || value.length > mostSelectedMessages) {
        return null;
    }

    const messages: string[] = [];
    for (const message of value) {
        if (!isIdentifier(message)) {
            return null;
        }

        messages.push(message);
    }

    return messages;
}

function scopeIn(value: unknown): MailScope | null {
    if (typeof value !== 'object' || value === null || Array.isArray(value)) {
        return null;
    }

    const record = value as Record<string, unknown>;
    const accountId = record['accountId'];
    const alias = record['alias'];

    switch (record['kind']) {
        case 'everything':
            return everything;
        case 'role':
            return isMailFolderRole(record['role']) ? { kind: 'role', role: record['role'] } : null;
        case 'account':
            return isIdentifier(accountId) ? { kind: 'account', accountId } : null;
        case 'folder':
            return isIdentifier(accountId) && isIdentifier(alias) ? { kind: 'folder', accountId, alias } : null;
        default:
            return null;
    }
}

// A name the service assigned rather than free text, so it is bounded here for the same reason every other stored value
// is: what is read back is held in state and written out again on every revision.
function isIdentifier(value: unknown): value is string {
    return typeof value === 'string' && value.length <= longestIdentifier;
}

function foldsToggledIn(value: unknown): readonly string[] | null {
    if (!Array.isArray(value) || value.length > mostToggledFolds) {
        return null;
    }

    const rows: string[] = [];
    for (const row of value) {
        if (typeof row !== 'string' || row.length > longestRow) {
            return null;
        }

        rows.push(row);
    }

    return rows;
}
