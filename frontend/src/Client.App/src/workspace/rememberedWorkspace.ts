// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { everything, isMailFolderRole, type MailScope } from './mailScope';
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
// Reached as `window.sessionStorage` rather than as the bare global for the reason `localization/locale.ts` gives:
// Node publishes stores of its own that win over the document's under the test runner.
const storageKey = 'mailfathom.workspace';

// What a stored workspace may carry before it is read as somebody's edit rather than as this client's own writing. A
// tree holds tens of rows, a question is a sentence, and an identifier — a message, an account, a folder alias — is a
// name the service assigned, so each of the three is far above anything the client itself writes there.
const mostCollapsedRows = 512;
const longestQuestion = 4_096;
const longestIdentifier = 256;

// What a selection may hold before it is read as somebody's edit. The list keeps a bounded number of pages, so a reader
// who selects every row it is holding selects a few hundred; this is above that and bounds what one tab writes here.
const mostSelectedMessages = 1_024;

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
 * Everything but the selected fragment, which is a passage of somebody's mail rather than a name the service assigned:
 * keeping it would put mail content in a browser store for nothing, since the reading pane drops the fragment as the
 * message it belongs to opens and a reload is that message opening again.
 */
export function rememberWorkspace(workspace: Workspace): void {
    try {
        window.sessionStorage.setItem(storageKey, JSON.stringify({ ...workspace, fragment: null }));
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
    const collapsed = collapsedIn(record['collapsed']);
    const selection = record['selection'] ?? null;
    const selected = selectedIn(record['selected']);
    const question = record['question'];

    if (scope === null || collapsed === null || selected === null) {
        return null;
    }

    if (selection !== null && !isIdentifier(selection)) {
        return null;
    }

    if (typeof question !== 'string' || question.length > longestQuestion) {
        return null;
    }

    return { scope, collapsed, selection, fragment: null, selected, question };
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

function collapsedIn(value: unknown): readonly string[] | null {
    if (!Array.isArray(value) || value.length > mostCollapsedRows) {
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
